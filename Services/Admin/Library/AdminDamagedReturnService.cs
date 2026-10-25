using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Returns;
using CDM_OneServe_API.Interfaces.Admin.Library;
using CDM_OneServe_API.Models.Library;
using CDM_OneServe_API.Services.Notifications;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Library;

public class AdminDamagedReturnService : IAdminDamagedReturnService
{
    private readonly AppDbContext _db;
    private readonly IAdminLibraryActivityLogger _activity;
    private readonly IPushDispatcher _push;

    public AdminDamagedReturnService(
        AppDbContext db,
        IAdminLibraryActivityLogger activity,
        IPushDispatcher push)
    {
        _db = db;
        _activity = activity;
        _push = push;
    }

    // =====================================================
    // LIST
    // =====================================================

    public async Task<PagedResult<DamagedReturnDto>> GetAsync(
        DamagedReturnFilterDto filter, CancellationToken cancellationToken)
    {
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var today = LibraryClock.Now.Date;
        var status = (filter.Status ?? "pending").Trim().ToLowerInvariant();

        var query = _db.BorrowTransactions.AsNoTracking()
            .Include(t => t.User)
            .Include(t => t.Book)
            .Where(t => t.ReturnDate != null && t.ReturnCondition == "Damaged");

        if (status == "pending")
        {
            query = query.Where(t =>
                t.Remarks == null || !t.Remarks.Contains("[damaged-resolved:"));
        }
        else if (status == "resolved")
        {
            query = query.Where(t =>
                t.Remarks != null && t.Remarks.Contains("[damaged-resolved:"));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            var hasId = int.TryParse(search.TrimStart('#'), out var transactionId);

            query = query.Where(t =>
                (hasId && t.BorrowId == transactionId) ||
                (t.User != null && t.User.FullName.Contains(search)) ||
                (t.User != null && t.User.IdNumber != null && t.User.IdNumber.Contains(search)) ||
                (t.Book != null && t.Book.Title.Contains(search)) ||
                (t.Book != null && t.Book.ISBN.Contains(search)));
        }

        // The deadline lives in a Remarks marker, so ordering is done after
        // the rows are materialised. Damaged returns are a small queue.
        var rows = await query
            .OrderByDescending(t => t.ReturnDate)
            .Take(1000)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(t => ToDto(t, today))
            .OrderBy(x => x.IsResolved)
            .ThenBy(x => x.ReplacementDueDate ?? DateTime.MaxValue)
            .ThenByDescending(x => x.ReturnDate)
            .ToList();

        var total = items.Count;

        return new PagedResult<DamagedReturnDto>
        {
            Items = items.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    // =====================================================
    // SET REPLACEMENT DEADLINE
    // =====================================================

    public async Task<LibraryResult<DamagedReturnDto>> SetDueDateAsync(
        int transactionId, SetDamagedDueDateDto dto, int staffId, CancellationToken cancellationToken)
    {
        var loan = await LoadAsync(transactionId, cancellationToken);

        if (loan == null)
            return LibraryResult<DamagedReturnDto>.Failure(404, "Damaged return not found.");

        if (LibraryDamagedReturns.IsResolved(loan.Remarks))
            return LibraryResult<DamagedReturnDto>.Failure(409, "This damaged return is already resolved.");

        var dueDate = dto.ReplacementDueDate.Date;
        var today = LibraryClock.Now.Date;

        if (dueDate < today)
            return LibraryResult<DamagedReturnDto>.Failure(400, "The replacement deadline cannot be in the past.");

        loan.Remarks = LibraryDamagedReturns.WithDueDate(loan.Remarks, dueDate);

        _db.LibraryNotifications.Add(new LibraryNotification
        {
            UserId = loan.UserId,
            Type = "book-damaged",
            Title = "Replacement due date set",
            Message =
                $"The damaged copy of '{loan.Book?.Title ?? "a book"}' must be returned or replaced by {dueDate:MMM d, yyyy}.",
            IsRead = false,
            CreatedAt = DateTime.Now
        });

        _activity.Add(staffId, "LIBRARY_DAMAGED_DUE_SET",
            $"set the damaged-copy deadline for \"{loan.Book?.Title ?? "a book"}\" to {dueDate:MMM d, yyyy}");

        await _db.SaveChangesAsync(cancellationToken);

        await _push.EnqueueAsync(new PushNotification(
            UserId: loan.UserId,
            Module: "Library",
            Type: "book-damaged",
            Title: "Damaged book replacement",
            Body: $"Please return or replace the damaged copy of '{loan.Book?.Title ?? "a book"}' by {dueDate:MMM d, yyyy}.",
            TargetUrl: PushRoutes.LibraryLoans,
            DedupeKey: $"damaged-due:{loan.BorrowId}:{dueDate:yyyyMMdd}"), cancellationToken);

        return LibraryResult<DamagedReturnDto>.Success(ToDto(loan, today));
    }

    // =====================================================
    // RESOLVE (replacement received / copy repaired)
    // =====================================================

    public async Task<LibraryResult<DamagedReturnDto>> ResolveAsync(
        int transactionId, int staffId, CancellationToken cancellationToken)
    {
        var loan = await LoadAsync(transactionId, cancellationToken);

        if (loan == null)
            return LibraryResult<DamagedReturnDto>.Failure(404, "Damaged return not found.");

        if (LibraryDamagedReturns.IsResolved(loan.Remarks))
            return LibraryResult<DamagedReturnDto>.Failure(409, "This damaged return is already resolved.");

        var today = LibraryClock.Now.Date;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        loan.Remarks = LibraryDamagedReturns.MarkResolved(loan.Remarks, today);

        // The replacement (or the repaired copy) is usable again, so one copy
        // goes back on the shelf. TotalCopies does not change.
        await LibraryStock.ReleaseCopyAsync(_db, loan.BookId, DateTime.Now, cancellationToken);

        _db.LibraryNotifications.Add(new LibraryNotification
        {
            UserId = loan.UserId,
            Type = "book-damaged",
            Title = "Damaged book resolved",
            Message = $"The damaged copy of '{loan.Book?.Title ?? "a book"}' has been settled. Thank you!",
            IsRead = false,
            CreatedAt = DateTime.Now
        });

        _activity.Add(staffId, "LIBRARY_DAMAGED_RESOLVED",
            $"resolved the damaged copy of \"{loan.Book?.Title ?? "a book"}\"");

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await _push.EnqueueAsync(new PushNotification(
            UserId: loan.UserId,
            Module: "Library",
            Type: "book-damaged",
            Title: "Damaged book resolved",
            Body: $"The damaged copy of '{loan.Book?.Title ?? "a book"}' has been settled.",
            TargetUrl: PushRoutes.LibraryLoans,
            DedupeKey: $"damaged-resolved:{loan.BorrowId}"), cancellationToken);

        return LibraryResult<DamagedReturnDto>.Success(ToDto(loan, today));
    }

    // =====================================================
    // HELPERS
    // =====================================================

    private Task<BorrowTransaction?> LoadAsync(int transactionId, CancellationToken cancellationToken) =>
        _db.BorrowTransactions
            .Include(t => t.User)
            .Include(t => t.Book)
            .FirstOrDefaultAsync(
                t => t.BorrowId == transactionId &&
                     t.ReturnDate != null &&
                     t.ReturnCondition == "Damaged",
                cancellationToken);

    private static DamagedReturnDto ToDto(BorrowTransaction loan, DateTime today)
    {
        var dueDate = LibraryDamagedReturns.GetDueDate(loan.Remarks);
        var resolvedDate = LibraryDamagedReturns.GetResolvedDate(loan.Remarks);

        return new DamagedReturnDto
        {
            TransactionId = loan.BorrowId,
            UserId = loan.UserId,
            PatronName = loan.User?.FullName ?? "(deleted account)",
            PatronId = loan.User?.IdNumber,
            PatronType = loan.User?.Role ?? string.Empty,
            BookId = loan.BookId,
            BookTitle = loan.Book?.Title ?? "(deleted book)",
            Author = loan.Book?.Author ?? string.Empty,
            ISBN = loan.Book?.ISBN,
            CoverImage = loan.Book?.CoverImage,
            ReturnDate = loan.ReturnDate ?? loan.DueDate,
            ReplacementDueDate = dueDate,
            IsOverdue = !resolvedDate.HasValue &&
                        dueDate.HasValue &&
                        dueDate.Value.Date < today,
            DaysLeft = dueDate.HasValue
                ? (dueDate.Value.Date - today).Days
                : null,
            IsResolved = resolvedDate.HasValue,
            ResolvedDate = resolvedDate
        };
    }
}
