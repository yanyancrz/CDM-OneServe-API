using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Reports;
using CDM_OneServe_API.Interfaces.Admin.Library;
using CDM_OneServe_API.Models.Library;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Library;

// READ ONLY. Reports never change data.
public class AdminReportService : IAdminReportService
{
    private const int MaxExportRows = 20000;
    private const int MaxRangeDays = 365;

    private static readonly string[] KnownInstitutes = { "ICS", "ITE", "IBE" };

    private readonly AppDbContext _db;

    public AdminReportService(AppDbContext db)
    {
        _db = db;
    }

    // Inclusive dates in, [from, toExclusive) out.
    private static (DateTime From, DateTime ToInclusive, DateTime ToExclusive) Range(ReportFilterDto f)
    {
        var to = (f.To ?? DateTime.Today).Date;
        var from = (f.From ?? to.AddDays(-29)).Date;

        if (from > to) (from, to) = (to, from);
        if ((to - from).TotalDays > MaxRangeDays) from = to.AddDays(-MaxRangeDays);

        return (from, to, to.AddDays(1));
    }

    private static string NormalizeGranularity(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "weekly" => "weekly",
            "monthly" => "monthly",
            _ => "daily"
        };

    private static DateTime BucketStart(DateTime date, string granularity) => granularity switch
    {
        "weekly" => date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7)), // Monday
        "monthly" => new DateTime(date.Year, date.Month, 1),
        _ => date.Date
    };

    private static DateTime NextBucket(DateTime bucket, string granularity) => granularity switch
    {
        "weekly" => bucket.AddDays(7),
        "monthly" => bucket.AddMonths(1),
        _ => bucket.AddDays(1)
    };

    // =====================================================
    // SUMMARY (KPIs)
    // =====================================================

    public async Task<ReportSummaryDto> GetSummaryAsync(ReportFilterDto f, CancellationToken cancellationToken)
    {
        var (from, to, toEx) = Range(f);
        var now = DateTime.Now;

        var loans = _db.BorrowTransactions.AsNoTracking();

        // Sequential on purpose: one DbContext cannot run queries in parallel.
        var borrowed = await loans.CountAsync(t => t.BorrowDate >= from && t.BorrowDate < toEx, cancellationToken);

        var returned = await loans.CountAsync(
            t => t.ReturnDate != null && t.ReturnDate >= from && t.ReturnDate < toEx, cancellationToken);

        var onTime = await loans.CountAsync(
            t => t.ReturnDate != null && t.ReturnDate >= from && t.ReturnDate < toEx && t.ReturnDate <= t.DueDate,
            cancellationToken);

        var active = await loans.CountAsync(t => t.ReturnDate == null, cancellationToken);

        var overdue = await loans.CountAsync(
            t => t.ReturnDate == null && t.DueDate < now, cancellationToken);

        return new ReportSummaryDto
        {
            From = from,
            To = to,
            TotalCirculation = borrowed,
            TotalReturns = returned,
            ActiveLoans = active,
            OverdueLoans = overdue,
            OnTimeReturnRate = returned == 0 ? null : Math.Round(onTime * 100.0 / returned, 1)
        };
    }

    // =====================================================
    // CIRCULATION TRENDS
    // =====================================================

    public async Task<CirculationReportDto> GetCirculationAsync(ReportFilterDto f, CancellationToken cancellationToken)
    {
        var (from, to, toEx) = Range(f);
        var granularity = NormalizeGranularity(f.Granularity);

        var borrowDates = await _db.BorrowTransactions.AsNoTracking()
            .Where(t => t.BorrowDate >= from && t.BorrowDate < toEx)
            .Select(t => t.BorrowDate)
            .ToListAsync(cancellationToken);

        var returnDates = await _db.BorrowTransactions.AsNoTracking()
            .Where(t => t.ReturnDate != null && t.ReturnDate >= from && t.ReturnDate < toEx)
            .Select(t => t.ReturnDate!.Value)
            .ToListAsync(cancellationToken);

        var borrowedBy = borrowDates.GroupBy(d => BucketStart(d, granularity)).ToDictionary(g => g.Key, g => g.Count());
        var returnedBy = returnDates.GroupBy(d => BucketStart(d, granularity)).ToDictionary(g => g.Key, g => g.Count());

        // Every bucket in the range appears, even with zero activity.
        var points = new List<CirculationPointDto>();

        for (var bucket = BucketStart(from, granularity); bucket <= to; bucket = NextBucket(bucket, granularity))
        {
            points.Add(new CirculationPointDto
            {
                Date = bucket,
                Borrowed = borrowedBy.GetValueOrDefault(bucket),
                Returned = returnedBy.GetValueOrDefault(bucket)
            });
        }

        return new CirculationReportDto
        {
            Granularity = granularity,
            From = from,
            To = to,
            TotalBorrowed = borrowDates.Count,
            TotalReturned = returnDates.Count,
            Points = points
        };
    }

    // =====================================================
    // INSTITUTE USAGE
    // =====================================================

    public async Task<List<InstituteReportDto>> GetInstitutesAsync(ReportFilterDto f, CancellationToken cancellationToken)
    {
        var (from, _, toEx) = Range(f);

        var institutes = await _db.BorrowTransactions.AsNoTracking()
            .Where(t => t.BorrowDate >= from && t.BorrowDate < toEx)
            .Select(t => t.Book != null ? t.Book.Institute : null)
            .ToListAsync(cancellationToken);

        var counts = institutes
            .GroupBy(i => string.IsNullOrWhiteSpace(i) ? "Unspecified" : i.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.Count());

        var result = KnownInstitutes
            .Select(i => new InstituteReportDto { Institute = i, Borrowed = counts.GetValueOrDefault(i) })
            .ToList();

        // Anything else found in the data (including Unspecified) is listed after.
        result.AddRange(counts
            .Where(kv => !KnownInstitutes.Contains(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new InstituteReportDto { Institute = kv.Key, Borrowed = kv.Value }));

        return result;
    }

    // =====================================================
    // MOST BORROWED BOOKS
    // =====================================================

    public async Task<List<TopBookDto>> GetTopBooksAsync(ReportFilterDto f, int limit, CancellationToken cancellationToken)
    {
        var (from, _, toEx) = Range(f);
        limit = Math.Clamp(limit, 1, 50);

        var top = await _db.BorrowTransactions.AsNoTracking()
            .Where(t => t.BorrowDate >= from && t.BorrowDate < toEx)
            .GroupBy(t => t.BookId)
            .Select(g => new { BookId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.BookId)
            .Take(limit)
            .ToListAsync(cancellationToken);

        if (top.Count == 0) return new List<TopBookDto>();

        var ids = top.Select(x => x.BookId).ToList();

        var books = await _db.Books.AsNoTracking()
            .Where(b => ids.Contains(b.BookId))
            .Select(b => new { b.BookId, b.Title, b.Author, b.Institute })
            .ToDictionaryAsync(b => b.BookId, cancellationToken);

        return top.Select(x =>
        {
            books.TryGetValue(x.BookId, out var b);

            return new TopBookDto
            {
                BookId = x.BookId,
                Title = b?.Title ?? "(deleted book)",
                Author = b?.Author ?? string.Empty,
                Institute = b?.Institute,
                BorrowCount = x.Count
            };
        }).ToList();
    }

    // =====================================================
    // TABLES (CSV exports: overdue, returns)
    // =====================================================

    private sealed record LoanRow(
        int BorrowId, string? PatronId, string? PatronName, string? PatronType, string? Title,
        DateTime BorrowDate, DateTime DueDate, DateTime? ReturnDate,
        string? Condition);

    private static IQueryable<LoanRow> LoanRows(IQueryable<BorrowTransaction> query) =>
        query.Select(t => new LoanRow(
            t.BorrowId,
            t.User != null ? t.User.IdNumber : null,
            t.User != null ? t.User.FullName : null,
            t.User != null ? t.User.Role : null,
            t.Book != null ? t.Book.Title : null,
            t.BorrowDate,
            t.DueDate,
            t.ReturnDate,
            t.ReturnCondition));

    private static string D(DateTime? date) => date?.ToString("yyyy-MM-dd") ?? string.Empty;
    private static string S(string? value) => value ?? string.Empty;

    private static int OverdueDays(DateTime due, DateTime? returned, DateTime now) =>
        Math.Max(((returned ?? now).Date - due.Date).Days, 0);

    public async Task<LibraryResult<ExportTableDto>> GetTableAsync(string dataset, ReportFilterDto f, CancellationToken cancellationToken)
    {
        var (from, to, toEx) = Range(f);
        var now = DateTime.Now;
        var loans = _db.BorrowTransactions.AsNoTracking();
        var period = $"{from:yyyy-MM-dd}_to_{to:yyyy-MM-dd}";

        ExportTableDto table;

        switch ((dataset ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "catalog":
                {
                    var books = await _db.Books.AsNoTracking()
                        .OrderBy(b => b.Title)
                        .Take(MaxExportRows)
                        .Select(b => new
                        {
                            b.Title,
                            b.Author,
                            b.ISBN,
                            b.CallNo,
                            b.Institute,
                            b.Category,
                            b.YearLevel,
                            b.Semester,
                            b.TotalCopies,
                            b.AvailableCopies,
                            b.Status
                        })
                        .ToListAsync(cancellationToken);

                    table = new ExportTableDto
                    {
                        FileName = $"library-book-catalog-{now:yyyy-MM-dd}",
                        Columns = new() { "Title", "Author", "ISBN", "Call No.", "Institute", "Category", "Year Level", "Semester", "Total Copies", "Available Copies", "Status" },
                        Rows = books.Select(b => new List<string>
                    {
                        b.Title, b.Author, b.ISBN, S(b.CallNo), S(b.Institute), S(b.Category),
                        S(b.YearLevel), S(b.Semester), b.TotalCopies.ToString(), b.AvailableCopies.ToString(), b.Status
                    }).ToList()
                    };
                    break;
                }

            case "active-loans":
            case "overdue":
                {
                    var overdueOnly = dataset.Trim().Equals("overdue", StringComparison.OrdinalIgnoreCase);

                    var query = loans.Where(t => t.ReturnDate == null);
                    if (overdueOnly) query = query.Where(t => t.DueDate < now);

                    var rows = await LoanRows(query.OrderBy(t => t.DueDate).Take(MaxExportRows))
                        .ToListAsync(cancellationToken);

                    table = new ExportTableDto
                    {
                        FileName = $"library-{(overdueOnly ? "overdue-books" : "active-loans")}-{now:yyyy-MM-dd}",
                        Columns = new() { "Transaction", "Patron ID", "Patron", "Type", "Book", "Borrowed", "Due", "Days Overdue" },
                        Rows = rows.Select(r =>
                        {
                            var days = OverdueDays(r.DueDate, null, now);

                            return new List<string>
                        {
                            r.BorrowId.ToString(), S(r.PatronId), S(r.PatronName), S(r.PatronType), S(r.Title),
                            D(r.BorrowDate), D(r.DueDate), days.ToString()
                        };
                        }).ToList()
                    };
                    break;
                }

            case "borrows":
                {
                    var rows = await LoanRows(loans
                            .Where(t => t.BorrowDate >= from && t.BorrowDate < toEx)
                            .OrderByDescending(t => t.BorrowDate)
                            .Take(MaxExportRows))
                        .ToListAsync(cancellationToken);

                    table = new ExportTableDto
                    {
                        FileName = $"library-borrow-transactions-{period}",
                        Columns = new() { "Transaction", "Patron ID", "Patron", "Type", "Book", "Borrowed", "Due", "Returned", "Status" },
                        Rows = rows.Select(r => new List<string>
                    {
                        r.BorrowId.ToString(), S(r.PatronId), S(r.PatronName), S(r.PatronType), S(r.Title),
                        D(r.BorrowDate), D(r.DueDate), D(r.ReturnDate),
                        r.ReturnDate != null ? "Returned" : r.DueDate < now ? "Overdue" : "Borrowed"
                    }).ToList()
                    };
                    break;
                }

            case "returns":
                {
                    var rows = await LoanRows(loans
                            .Where(t => t.ReturnDate != null && t.ReturnDate >= from && t.ReturnDate < toEx)
                            .OrderByDescending(t => t.ReturnDate)
                            .Take(MaxExportRows))
                        .ToListAsync(cancellationToken);

                    table = new ExportTableDto
                    {
                        FileName = $"library-return-transactions-{period}",
                        Columns = new() { "Transaction", "Patron ID", "Patron", "Book", "Borrowed", "Due", "Returned", "Days Overdue", "Condition" },
                        Rows = rows.Select(r => new List<string>
                    {
                        r.BorrowId.ToString(), S(r.PatronId), S(r.PatronName), S(r.Title),
                        D(r.BorrowDate), D(r.DueDate), D(r.ReturnDate),
                        OverdueDays(r.DueDate, r.ReturnDate, now).ToString(),
                        S(r.Condition)
                    }).ToList()
                    };
                    break;
                }

            case "reservations":
                {
                    var rows = await _db.Reservations.AsNoTracking()
                        .Where(r => r.ReservationAt >= from && r.ReservationAt < toEx)
                        .OrderByDescending(r => r.ReservationAt)
                        .Take(MaxExportRows)
                        .Select(r => new
                        {
                            r.ReservationId,
                            PatronId = r.User != null ? r.User.IdNumber : null,
                            PatronName = r.User != null ? r.User.FullName : null,
                            Title = r.Book != null ? r.Book.Title : null,
                            r.ReservationAt,
                            r.ExpirationDate,
                            r.Status,
                            r.Remarks
                        })
                        .ToListAsync(cancellationToken);

                    table = new ExportTableDto
                    {
                        FileName = $"library-reservations-{period}",
                        Columns = new() { "Reservation", "Patron ID", "Patron", "Book", "Reserved", "Expires", "Status", "Remarks" },
                        Rows = rows.Select(r => new List<string>
                    {
                        r.ReservationId.ToString(), S(r.PatronId), S(r.PatronName), S(r.Title),
                        D(r.ReservationAt), D(r.ExpirationDate), ReservationStatuses.Label(r.Status), S(r.Remarks)
                    }).ToList()
                    };
                    break;
                }

            default:
                return LibraryResult<ExportTableDto>.Failure(404, "Unknown report.");
        }

        return LibraryResult<ExportTableDto>.Success(table);
    }
}