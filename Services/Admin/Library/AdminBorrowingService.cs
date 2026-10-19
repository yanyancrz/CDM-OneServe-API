using System.Text.Json;

using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin.Library.Borrowing;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.Interfaces.Admin.Library;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Library;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Library;

public class AdminBorrowingService : IAdminBorrowingService
{
    private static readonly string[] PatronRoles = { "Student", "Faculty" };

    private readonly AppDbContext _db;
    private readonly IAdminLibraryActivityLogger _activity;

    public AdminBorrowingService(AppDbContext db, IAdminLibraryActivityLogger activity)
    {
        _db = db;
        _activity = activity;
    }

    private IQueryable<User> Patrons() =>
        _db.Users.AsNoTracking().Where(u => PatronRoles.Contains(u.Role));

    // =====================================================
    // LIST / DETAILS
    // =====================================================

    private sealed record Row(
        int BorrowId, int UserId, string? PatronName, string? PatronId, string? PatronType,
        int BookId, string? BookTitle, DateTime BorrowDate, DateTime DueDate,
        DateTime? ReturnDate);

    private IQueryable<Row> Rows(IQueryable<BorrowTransaction> query) =>
        query.Select(t => new Row(
            t.BorrowId,
            t.UserId,
            t.User != null ? t.User.FullName : null,
            t.User != null ? t.User.IdNumber : null,
            t.User != null ? t.User.Role : null,
            t.BookId,
            t.Book != null ? t.Book.Title : null,
            t.BorrowDate,
            t.DueDate,
            t.ReturnDate));

    private static BorrowListItemDto ToItem(Row r, DateTime now)
    {
        var active = r.ReturnDate == null;
        var overdue = active && r.DueDate < now;
        var days = overdue ? Math.Max((now.Date - r.DueDate.Date).Days, 0) : 0;

        return new BorrowListItemDto
        {
            TransactionId = r.BorrowId,
            UserId = r.UserId,
            PatronName = r.PatronName ?? "(deleted account)",
            PatronId = r.PatronId,
            PatronType = r.PatronType ?? string.Empty,
            BookId = r.BookId,
            BookTitle = r.BookTitle ?? "(deleted book)",
            BorrowDate = r.BorrowDate,
            DueDate = r.DueDate,
            ReturnDate = r.ReturnDate,
            Status = !active ? "Returned" : overdue ? "Overdue" : "Borrowed",
            OverdueDays = days
        };
    }

    public async Task<PagedResult<BorrowListItemDto>> GetBorrowingsAsync(BorrowFilterDto f, CancellationToken cancellationToken)
    {
        var page = Math.Max(f.Page, 1);
        var pageSize = Math.Clamp(f.PageSize, 1, 100);
        var now = DateTime.Now;

        var query = _db.BorrowTransactions.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            query = query.Where(t =>
                (t.User != null && t.User.FullName.Contains(s)) ||
                (t.User != null && t.User.IdNumber != null && t.User.IdNumber.Contains(s)) ||
                (t.Book != null && t.Book.Title.Contains(s)));
        }

        query = (f.Status ?? string.Empty).ToLowerInvariant() switch
        {
            "active" => query.Where(t => t.ReturnDate == null),
            "overdue" => query.Where(t => t.ReturnDate == null && t.DueDate < now),
            "returned" => query.Where(t => t.ReturnDate != null),
            _ => query
        };

        var total = await query.CountAsync(cancellationToken);

        var rows = await Rows(query.OrderByDescending(t => t.BorrowDate).Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(cancellationToken);

        return new PagedResult<BorrowListItemDto>
        {
            Items = rows.Select(r => ToItem(r, now)).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<LibraryResult<BorrowListItemDto>> GetBorrowingAsync(int id, CancellationToken cancellationToken)
    {
        var row = await Rows(_db.BorrowTransactions.AsNoTracking().Where(t => t.BorrowId == id))
            .FirstOrDefaultAsync(cancellationToken);

        return row == null
            ? LibraryResult<BorrowListItemDto>.Failure(404, "Borrow transaction not found.")
            : LibraryResult<BorrowListItemDto>.Success(ToItem(row, DateTime.Now));
    }

    // =====================================================
    // PATRON LOOKUP
    // =====================================================

    public async Task<List<PatronLookupDto>> SearchPatronsAsync(string? search, CancellationToken cancellationToken)
    {
        var s = (search ?? string.Empty).Trim();
        if (s.Length < 2) return new List<PatronLookupDto>();

        var users = await Patrons()
            .Where(u =>
                u.FullName.Contains(s) ||
                (u.IdNumber != null && u.IdNumber.Contains(s)) ||
                (u.Email != null && u.Email.Contains(s)))
            .OrderBy(u => u.FullName)
            .Take(8)
            .ToListAsync(cancellationToken);

        var result = new List<PatronLookupDto>();

        foreach (var user in users)
            result.Add(await BuildPatronAsync(user, cancellationToken));

        return result;
    }

    public async Task<LibraryResult<PatronLookupDto>> GetPatronAsync(string identifier, CancellationToken cancellationToken)
    {
        var raw = (identifier ?? string.Empty).Trim();

        if (raw.Length == 0)
            return LibraryResult<PatronLookupDto>.Failure(400, "Enter a student ID or employee ID.");

        User? user;

        if (raw.StartsWith("uid:", StringComparison.OrdinalIgnoreCase) && int.TryParse(raw[4..], out var uid))
            user = await Patrons().FirstOrDefaultAsync(u => u.Id == uid, cancellationToken);
        else
            user = await FindByIdNumberAsync(raw, cancellationToken);

        return user == null
            ? LibraryResult<PatronLookupDto>.Failure(404, "No matching student or faculty account was found.")
            : LibraryResult<PatronLookupDto>.Success(await BuildPatronAsync(user, cancellationToken));
    }

    public async Task<LibraryResult<PatronLookupDto>> ScanPatronAsync(string qrData, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(qrData))
            return LibraryResult<PatronLookupDto>.Failure(400, "No QR data was provided.");

        qrData = qrData.Trim();
        User? user = null;

        // Legacy faculty QR: CDM-FACULTY:<Employee ID>:<Name>:<Department>
        if (qrData.StartsWith("CDM-FACULTY:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = qrData.Split(':');

            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                return LibraryResult<PatronLookupDto>.Failure(400, "Invalid faculty QR format.");

            user = await FindByIdNumberAsync(parts[1], cancellationToken);
        }
        else
        {
            try
            {
                using var document = JsonDocument.Parse(qrData);
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                    return LibraryResult<PatronLookupDto>.Failure(400, "The QR code format is not recognized.");

                if (TryGetInt(root, "userId", out var userId))
                    user = await Patrons().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

                if (user == null)
                {
                    var idNumber =
                        GetString(root, "idNumber") ??
                        GetString(root, "studentNumber") ??
                        GetString(root, "employeeId");

                    if (!string.IsNullOrWhiteSpace(idNumber))
                        user = await FindByIdNumberAsync(idNumber, cancellationToken);
                }
            }
            catch (JsonException)
            {
                return LibraryResult<PatronLookupDto>.Failure(400, "The QR code format is not recognized.");
            }
        }

        // The QR only finds the account. Everything returned comes from the database.
        return user == null
            ? LibraryResult<PatronLookupDto>.Failure(404, "No matching student or faculty account was found.")
            : LibraryResult<PatronLookupDto>.Success(await BuildPatronAsync(user, cancellationToken));
    }

    // =====================================================
    // BORROW (one database transaction)
    // =====================================================

    public async Task<LibraryResult<BorrowResponseDto>> BorrowAsync(BorrowRequestDto dto, int staffId, CancellationToken cancellationToken)
    {
        if (dto.UserId <= 0 || dto.BookId <= 0)
            return LibraryResult<BorrowResponseDto>.Failure(400, "Choose a patron and a book.");

        if (!LibraryDefaults.LoanDayOptions.Contains(dto.LoanDays))
            return LibraryResult<BorrowResponseDto>.Failure(400,
                $"Choose a loan period of {string.Join(", ", LibraryDefaults.LoanDayOptions)} days.");

        if (!dto.AcceptedTerms)
            return LibraryResult<BorrowResponseDto>.Failure(400, "The borrowing terms must be accepted.");

        var user = await Patrons().FirstOrDefaultAsync(u => u.Id == dto.UserId, cancellationToken);
        if (user == null) return LibraryResult<BorrowResponseDto>.Failure(404, "Patron not found.");

        var book = await _db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.BookId == dto.BookId, cancellationToken);
        if (book == null) return LibraryResult<BorrowResponseDto>.Failure(404, "Book not found.");

        // Anything that returns or throws after this point rolls the transaction back.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var limit = LibraryDefaults.BorrowLimitFor(user.Role);

        var activeLoans = await _db.BorrowTransactions.AsNoTracking()
            .CountAsync(t => t.UserId == user.Id && t.ReturnDate == null, cancellationToken);

        var reason = CheckEligibility(user, activeLoans, limit);
        if (reason != null) return LibraryResult<BorrowResponseDto>.Failure(409, reason);

        if (await _db.BorrowTransactions.AnyAsync(
                t => t.UserId == user.Id && t.BookId == book.BookId && t.ReturnDate == null, cancellationToken))
            return LibraryResult<BorrowResponseDto>.Failure(409, "This patron already has this book on loan.");

        var now = DateTime.Now;

        // Atomic: two people borrowing the last copy at the same time cannot both succeed.
        var taken = await _db.Books
            .Where(b => b.BookId == book.BookId && b.AvailableCopies > 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.AvailableCopies, b => b.AvailableCopies - 1)
                .SetProperty(b => b.UpdatedAt, now), cancellationToken);

        if (taken == 0)
            return LibraryResult<BorrowResponseDto>.Failure(409, "No copy of this book is available right now.");

        await _db.Books
            .Where(b => b.BookId == book.BookId && b.AvailableCopies <= 0)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, "Unavailable"), cancellationToken);

        var due = DueDateFor(now, dto.LoanDays);

        var loan = new BorrowTransaction
        {
            UserId = user.Id,
            BookId = book.BookId,
            BorrowDate = now,
            DueDate = due,
            Status = "Borrowed",
            RenewalCount = 0,
            CreatedAt = now
        };

        _db.BorrowTransactions.Add(loan);

        _activity.Add(staffId, "LIBRARY_BOOK_BORROWED", $"issued \"{book.Title}\" to {user.FullName}");

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var staffName = await _db.Users.AsNoTracking()
            .Where(u => u.Id == staffId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Library staff";

        return LibraryResult<BorrowResponseDto>.Success(new BorrowResponseDto
        {
            TransactionId = loan.BorrowId,
            UserId = user.Id,
            PatronName = user.FullName,
            PatronId = user.IdNumber,
            PatronType = user.Role,
            BookId = book.BookId,
            BookTitle = book.Title,
            Author = book.Author,
            ISBN = book.ISBN,
            CallNo = book.CallNo,
            BorrowDate = now,
            DueDate = due,
            LoanDays = dto.LoanDays,
            RemainingSlots = Math.Max(0, limit - (activeLoans + 1)),
            ProcessedBy = staffName
        }, 201);
    }

    // =====================================================
    // HELPERS
    // =====================================================

    // Due at the end of the last loan day. Shared with reservation claims (Part 8).
    internal static DateTime DueDateFor(DateTime now, int loanDays) =>
        now.Date.AddDays(loanDays + 1).AddSeconds(-1);

    // Shared with reservation claims (Part 8), so both use the same rules.
    internal static string? CheckEligibility(User user, int activeLoans, int limit)
    {
        if (!user.IsVerified) return "Account is not verified.";
        if (!user.IsProfileComplete) return "Account profile is incomplete.";

        // The database uses "Approved"; the admin screens use "Active". Both are allowed.
        var status = user.AccountStatus?.Trim() ?? string.Empty;

        var allowed =
            status.Equals("Active", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Approved", StringComparison.OrdinalIgnoreCase);

        if (!allowed)
            return $"Account is not active (status: {(status.Length == 0 ? "unknown" : status)}).";

        if (LibraryDefaults.IsBanned(user.BorrowBanUntil, DateTime.Now))
            return $"Borrowing is suspended for this semester (until {user.BorrowBanUntil:MMM d, yyyy}).";

        if (activeLoans >= limit)
            return $"Borrowing limit reached ({activeLoans}/{limit} books).";

        return null;
    }

    private async Task<PatronLookupDto> BuildPatronAsync(User user, CancellationToken cancellationToken)
    {
        var now = DateTime.Now;

        var active = await _db.BorrowTransactions.AsNoTracking()
            .CountAsync(t => t.UserId == user.Id && t.ReturnDate == null, cancellationToken);

        var overdue = await _db.BorrowTransactions.AsNoTracking()
            .CountAsync(t => t.UserId == user.Id && t.ReturnDate == null && t.DueDate < now, cancellationToken);

        var limit = LibraryDefaults.BorrowLimitFor(user.Role);
        var reason = CheckEligibility(user, active, limit);

        return new PatronLookupDto
        {
            UserId = user.Id,
            IdNumber = user.IdNumber,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role,
            Institute = user.Institute,
            Course = user.Course,
            YearLevel = user.YearLevel,
            AccountStatus = user.AccountStatus,
            BorrowLimit = limit,
            ActiveLoans = active,
            OverdueLoans = overdue,
            RemainingSlots = Math.Max(0, limit - active),
            CanBorrow = reason == null,
            Reason = reason,
            BannedUntil = user.BorrowBanUntil
        };
    }

    private Task<User?> FindByIdNumberAsync(string? rawId, CancellationToken cancellationToken)
    {
        var id = (rawId ?? string.Empty).Trim().Replace(" ", "").ToUpperInvariant();

        if (id.Length == 0) return Task.FromResult<User?>(null);

        return Patrons().FirstOrDefaultAsync(
            u => u.IdNumber != null && u.IdNumber.Replace(" ", "").ToUpper() == id,
            cancellationToken);
    }

    private static bool TryGetInt(JsonElement root, string name, out int value)
    {
        value = 0;

        if (!root.TryGetProperty(name, out var element)) return false;

        if (element.ValueKind == JsonValueKind.Number) return element.TryGetInt32(out value);
        if (element.ValueKind == JsonValueKind.String) return int.TryParse(element.GetString(), out value);

        return false;
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
}