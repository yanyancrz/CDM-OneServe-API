using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Returns;
using CDM_OneServe_API.Interfaces.Admin.Library;
using CDM_OneServe_API.Models.Library;
using CDM_OneServe_API.Services.Notifications;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Library;

public class AdminReturnService : IAdminReturnService
{
    private static readonly string[] Conditions = { "Good", "Damaged", "Lost" };
    private static readonly string[] PaymentStatuses = { "Paid", "Unpaid", "AddedToAccount" };

    private readonly AppDbContext _db;
    private readonly IAdminLibraryActivityLogger _activity;
    private readonly IPushDispatcher _push;

    public AdminReturnService(
        AppDbContext db,
        IAdminLibraryActivityLogger activity,
        IPushDispatcher push)
    {
        _db = db;
        _activity = activity;
        _push = push;
    }

    // The single place the fine is calculated.
    private static (int Days, decimal Fine) ComputeFine(DateTime dueDate, DateTime returnDate)
    {
        var days = Math.Max((returnDate.Date - dueDate.Date).Days, 0);
        return (days, days * LibraryDefaults.OverdueFinePerDay);
    }

    private sealed record Row(
        int BorrowId, int UserId, string? PatronName, string? PatronId, string? PatronType,
        int BookId, string? BookTitle, string? Author, string? CallNo,
        DateTime BorrowDate, DateTime DueDate);

    private static IQueryable<Row> Rows(IQueryable<BorrowTransaction> query) =>
        query.Select(t => new Row(
            t.BorrowId,
            t.UserId,
            t.User != null ? t.User.FullName : null,
            t.User != null ? t.User.IdNumber : null,
            t.User != null ? t.User.Role : null,
            t.BookId,
            t.Book != null ? t.Book.Title : null,
            t.Book != null ? t.Book.Author : null,
            t.Book != null ? t.Book.CallNo : null,
            t.BorrowDate,
            t.DueDate));

    private static ReturnLoanDto ToDto(Row r, DateTime now)
    {
        var (days, fine) = ComputeFine(r.DueDate, now);

        return new ReturnLoanDto
        {
            TransactionId = r.BorrowId,
            UserId = r.UserId,
            PatronName = r.PatronName ?? "(deleted account)",
            PatronId = r.PatronId,
            PatronType = r.PatronType ?? string.Empty,
            BookId = r.BookId,
            BookTitle = r.BookTitle ?? "(deleted book)",
            Author = r.Author ?? string.Empty,
            CallNo = r.CallNo,
            BorrowDate = r.BorrowDate,
            DueDate = r.DueDate,
            IsOverdue = r.DueDate < now,
            OverdueDays = days,
            Fine = fine,
            FinePerDay = LibraryDefaults.OverdueFinePerDay
        };
    }

    // =====================================================
    // LIST / LOOKUP
    // =====================================================

    public async Task<PagedResult<ReturnLoanDto>> GetActiveLoansAsync(ReturnFilterDto f, CancellationToken cancellationToken)
    {
        var page = Math.Max(f.Page, 1);
        var pageSize = Math.Clamp(f.PageSize, 1, 100);
        var now = DateTime.Now;

        var query = _db.BorrowTransactions.AsNoTracking().Where(t => t.ReturnDate == null);

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            var hasId = int.TryParse(s.TrimStart('#'), out var transactionId);

            query = query.Where(t =>
                (hasId && t.BorrowId == transactionId) ||
                (t.User != null && t.User.FullName.Contains(s)) ||
                (t.User != null && t.User.IdNumber != null && t.User.IdNumber.Contains(s)) ||
                (t.Book != null && t.Book.Title.Contains(s)));
        }

        if (string.Equals(f.Status, "overdue", StringComparison.OrdinalIgnoreCase))
            query = query.Where(t => t.DueDate < now);

        var total = await query.CountAsync(cancellationToken);

        var rows = await Rows(query.OrderBy(t => t.DueDate).Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(cancellationToken);

        return new PagedResult<ReturnLoanDto>
        {
            Items = rows.Select(r => ToDto(r, now)).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<LibraryResult<ReturnLoanDto>> LookupAsync(int transactionId, CancellationToken cancellationToken)
    {
        var row = await Rows(_db.BorrowTransactions.AsNoTracking()
                .Where(t => t.BorrowId == transactionId && t.ReturnDate == null))
            .FirstOrDefaultAsync(cancellationToken);

        return row == null
            ? LibraryResult<ReturnLoanDto>.Failure(404, "No active loan was found for this transaction.")
            : LibraryResult<ReturnLoanDto>.Success(ToDto(row, DateTime.Now));
    }

    // =====================================================
    // RETURN (one database transaction)
    // =====================================================

    public async Task<LibraryResult<ReturnResultDto>> ReturnAsync(ReturnBookDto dto, int staffId, CancellationToken cancellationToken)
    {
        var condition = dto.Condition?.Trim() ?? string.Empty;
        var payment = string.IsNullOrWhiteSpace(dto.PaymentStatus) ? null : dto.PaymentStatus.Trim();

        if (!Conditions.Contains(condition))
            return LibraryResult<ReturnResultDto>.Failure(400, "Choose the book condition: Good, Damaged, or Lost.");

        if (payment != null && !PaymentStatuses.Contains(payment))
            return LibraryResult<ReturnResultDto>.Failure(400, "Choose a valid payment status.");

        var loan = await _db.BorrowTransactions.AsNoTracking()
            .Include(t => t.User)
            .Include(t => t.Book)
            .FirstOrDefaultAsync(t => t.BorrowId == dto.TransactionId, cancellationToken);

        if (loan == null) return LibraryResult<ReturnResultDto>.Failure(404, "Borrow transaction not found.");
        if (loan.ReturnDate != null) return LibraryResult<ReturnResultDto>.Failure(409, "This book was already returned.");

        var now = DateTime.Now;
        var (days, fine) = ComputeFine(loan.DueDate, now);

        // A fine needs a payment status. No fine means nothing to record.
        if (fine > 0 && payment == null)
            return LibraryResult<ReturnResultDto>.Failure(400, "Choose the fine payment status.");

        if (fine == 0) payment = null;

        var title = loan.Book?.Title ?? "the book";
        var patron = loan.User?.FullName ?? "a patron";

        var note = $"Returned: {condition}";
        var remarks = string.IsNullOrWhiteSpace(loan.Remarks) ? note : $"{loan.Remarks} | {note}";

        // Anything that returns or throws before Commit rolls everything back.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Race-safe: only one request can close this loan.
        var changed = await _db.BorrowTransactions
            .Where(t => t.BorrowId == dto.TransactionId && t.ReturnDate == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.ReturnDate, now)
                .SetProperty(t => t.Status, "Returned")
                .SetProperty(t => t.Fine, fine)
                .SetProperty(t => t.ReturnCondition, condition)
                .SetProperty(t => t.FinePaymentStatus, payment)
                .SetProperty(t => t.Remarks, remarks), cancellationToken);

        if (changed == 0)
            return LibraryResult<ReturnResultDto>.Failure(409, "This loan was just updated. Refresh and try again.");

        var backOnShelf = false;

        if (condition == "Good")
        {
            await LibraryStock.ReleaseCopyAsync(_db, loan.BookId, now, cancellationToken);
            backOnShelf = true;
        }
        else if (condition == "Lost")
        {
            // The copy is gone for good: the catalog total goes down by one.
            await _db.Books
                .Where(b => b.BookId == loan.BookId && b.TotalCopies > 0)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.TotalCopies, b => b.TotalCopies - 1)
                    .SetProperty(b => b.UpdatedAt, now), cancellationToken);
        }
        // Damaged: the copy stays off the shelf until the Head fixes the totals.

        _db.LibraryActivities.Add(new LibraryActivity
        {
            UserId = loan.UserId,
            BookId = loan.BookId,
            ActivityType = "ReturnBook",
            Title = "Book Returned",
            Description = $"You returned '{title}'.",
            CreatedAt = now
        });

        _db.LibraryNotifications.Add(new LibraryNotification
        {
            UserId = loan.UserId,
            Type = "book-returned",
            Title = "Book Returned",
            Message = fine > 0
                ? $"'{title}' was returned. Overdue fine: PHP {fine:N2} ({payment})."
                : $"'{title}' was returned. Thank you!",
            IsRead = false,
            CreatedAt = now
        });

        var returnMessage = fine > 0
            ? $"'{title}' was returned. Overdue fine: PHP {fine:N2} ({payment})."
            : $"'{title}' was returned. Thank you!";

        await _push.EnqueueAsync(new PushNotification(
            UserId: loan.UserId,
            Module: "Library",
            Type: "book-returned",
            Title: "Book Returned",
            Body: returnMessage,
            TargetUrl: PushRoutes.Resolve("Library", "book-returned"),
            DedupeKey: $"bookreturned:{loan.BorrowId}"), cancellationToken);

        _activity.Add(staffId, "LIBRARY_BOOK_RETURNED",
            $"received the return of \"{title}\" from {patron} ({condition})");

        if (fine > 0)
            _activity.Add(staffId, "LIBRARY_FINE_RECORDED",
                $"recorded a fine of PHP {fine:N2} for {patron} ({payment})");

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var staffName = await _db.Users.AsNoTracking()
            .Where(u => u.Id == staffId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Library staff";

        return LibraryResult<ReturnResultDto>.Success(new ReturnResultDto
        {
            TransactionId = loan.BorrowId,
            PatronName = patron,
            PatronId = loan.User?.IdNumber,
            BookTitle = title,
            BorrowDate = loan.BorrowDate,
            DueDate = loan.DueDate,
            ReturnDate = now,
            OverdueDays = days,
            Fine = fine,
            FinePerDay = LibraryDefaults.OverdueFinePerDay,
            Condition = condition,
            PaymentStatus = payment,
            CopyReturnedToShelf = backOnShelf,
            ProcessedBy = staffName
        });
    }
}