using System.Globalization;

using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin.Library.Borrowing;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Reservations;
using CDM_OneServe_API.Interfaces.Admin.Library;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Library;
using CDM_OneServe_API.Services.Notifications;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Library;

public class AdminReservationService : IAdminReservationService
{
    private static readonly string[] PatronRoles = { "Student", "Faculty" };

    private const int MaxReasonLength = 300;

    private const string ChangedByOthers =
        "This reservation was just updated by someone else. Refresh and try again.";

    private readonly AppDbContext _db;
    private readonly IAdminLibraryActivityLogger _activity;
    private readonly IPushDispatcher _push;

    public AdminReservationService(
        AppDbContext db,
        IAdminLibraryActivityLogger activity,
        IPushDispatcher push)
    {
        _db = db;
        _activity = activity;
        _push = push;
    }

    // =====================================================
    // LIST / SUMMARY / DETAILS
    // =====================================================

    private sealed record Row(
        int ReservationId, int UserId, string? PatronName, string? PatronId, string? PatronType, string? Institute,
        int BookId, string? BookTitle, string? Author, string? CoverImage, int AvailableCopies,
        DateTime ReservationAt, DateTime ExpirationDate, string Status);

    private static IQueryable<Row> Rows(IQueryable<Reservation> query) =>
        query.Select(r => new Row(
            r.ReservationId,
            r.UserId,
            r.User != null ? r.User.FullName : null,
            r.User != null ? r.User.IdNumber : null,
            r.User != null ? r.User.Role : null,
            r.User != null ? r.User.Institute : null,
            r.BookId,
            r.Book != null ? r.Book.Title : null,
            r.Book != null ? r.Book.Author : null,
            r.Book != null ? r.Book.CoverImage : null,
            r.Book != null ? r.Book.AvailableCopies : 0,
            r.ReservationAt,
            r.ExpirationDate,
            r.Status));

    private static ReservationListItemDto ToItem(Row row, DateTime now)
    {
        var item = new ReservationListItemDto();
        Fill(item, row, now);
        return item;
    }

    // Shared by the list item and the details DTO. All "Can..." flags are decided here.
    private static void Fill(ReservationListItemDto item, Row row, DateTime now)
    {
        var active = ReservationStatuses.IsActive(row.Status);
        var expired = active && row.ExpirationDate < now;
        var open = active && !expired;
        var holdsCopy = ReservationStatuses.HoldsCopy(row.Status);

        item.ReservationId = row.ReservationId;
        item.UserId = row.UserId;
        item.PatronName = row.PatronName ?? "(deleted account)";
        item.PatronId = row.PatronId;
        item.PatronType = row.PatronType ?? string.Empty;
        item.Institute = row.Institute;
        item.BookId = row.BookId;
        item.BookTitle = row.BookTitle ?? "(deleted book)";
        item.Author = row.Author ?? string.Empty;
        item.CoverImage = row.CoverImage;
        item.AvailableCopies = row.AvailableCopies;
        item.ReservationAt = row.ReservationAt;
        item.ExpirationDate = row.ExpirationDate;

        item.Status = row.Status;
        item.DisplayStatus = expired ? ReservationStatuses.Expired : row.Status;
        item.IsExpired = expired || row.Status == ReservationStatuses.Expired;
        item.HoldsCopy = holdsCopy;

        item.CanApprove = open && row.Status == ReservationStatuses.Pending;
        item.CanMarkReady = open && ReservationStatuses.AwaitingAction.Contains(row.Status) && row.AvailableCopies > 0;
        item.CanReject = open && ReservationStatuses.AwaitingAction.Contains(row.Status);
        item.CanCancel = open;
        item.CanClaim = open && ReservationStatuses.IsClaimable(row.Status) && (holdsCopy || row.AvailableCopies > 0);
    }

    public async Task<PagedResult<ReservationListItemDto>> GetReservationsAsync(ReservationFilterDto f, CancellationToken cancellationToken)
    {
        var page = Math.Max(f.Page, 1);
        var pageSize = Math.Clamp(f.PageSize, 1, 100);
        var now = DateTime.Now;

        var query = _db.Reservations.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            var hasNumber = int.TryParse(s.TrimStart('#'), out var number);

            query = query.Where(r =>
                (hasNumber && r.ReservationId == number) ||
                (r.User != null && r.User.FullName.Contains(s)) ||
                (r.User != null && r.User.IdNumber != null && r.User.IdNumber.Contains(s)) ||
                (r.Book != null && r.Book.Title.Contains(s)) ||
                (r.Book != null && r.Book.ISBN.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(f.PatronType))
        {
            var type = f.PatronType.Trim();
            query = query.Where(r => r.User != null && r.User.Role == type);
        }

        query = ApplyStatus(query, f.Status, now);

        query = (f.Sort ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "oldest" => query.OrderBy(r => r.ReservationAt).ThenBy(r => r.ReservationId),
            "expiring" => query.OrderBy(r => r.ExpirationDate).ThenBy(r => r.ReservationId),
            _ => query.OrderByDescending(r => r.ReservationAt).ThenByDescending(r => r.ReservationId)
        };

        var total = await query.CountAsync(cancellationToken);

        var rows = await Rows(query.Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(cancellationToken);

        return new PagedResult<ReservationListItemDto>
        {
            Items = rows.Select(row => ToItem(row, now)).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    private static IQueryable<Reservation> ApplyStatus(IQueryable<Reservation> query, string? status, DateTime now)
    {
        var value = (status ?? string.Empty).Trim();

        if (value.Length == 0) return query;

        if (value.Equals("active", StringComparison.OrdinalIgnoreCase))
            return query.Where(r => ReservationStatuses.Active.Contains(r.Status) && r.ExpirationDate >= now);

        if (value.Equals(ReservationStatuses.Expired, StringComparison.OrdinalIgnoreCase))
            return query.Where(r =>
                r.Status == ReservationStatuses.Expired ||
                (ReservationStatuses.Active.Contains(r.Status) && r.ExpirationDate < now));

        if (value.Equals("completed", StringComparison.OrdinalIgnoreCase))
            value = ReservationStatuses.Claimed;

        var match = ReservationStatuses.All.FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase));

        if (match == null) return query.Where(r => false);

        // Active statuses whose date already passed are shown under Expired, so leave them out here.
        return ReservationStatuses.IsActive(match)
            ? query.Where(r => r.Status == match && r.ExpirationDate >= now)
            : query.Where(r => r.Status == match);
    }

    public async Task<ReservationSummaryDto> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        var endOfToday = now.Date.AddDays(1);

        // Sequential on purpose: a DbContext cannot run queries in parallel.
        var byStatus = await _db.Reservations.AsNoTracking()
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var pastDue = await _db.Reservations.AsNoTracking()
            .Where(r => ReservationStatuses.Active.Contains(r.Status) && r.ExpirationDate < now)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var expiringToday = await _db.Reservations.AsNoTracking()
            .CountAsync(r =>
                ReservationStatuses.Active.Contains(r.Status) &&
                r.ExpirationDate >= now &&
                r.ExpirationDate < endOfToday, cancellationToken);

        int Stored(string status) => byStatus.Where(x => x.Status == status).Sum(x => x.Count);
        int Past(string status) => pastDue.Where(x => x.Status == status).Sum(x => x.Count);
        int Open(string status) => Stored(status) - Past(status);

        var summary = new ReservationSummaryDto
        {
            Total = byStatus.Sum(x => x.Count),
            Reserved = Open(ReservationStatuses.Reserved),
            Pending = Open(ReservationStatuses.Pending),
            Approved = Open(ReservationStatuses.Approved),
            ReadyForPickup = Open(ReservationStatuses.ReadyForPickup),
            Claimed = Stored(ReservationStatuses.Claimed),
            Rejected = Stored(ReservationStatuses.Rejected),
            Cancelled = Stored(ReservationStatuses.Cancelled),
            Expired = Stored(ReservationStatuses.Expired) + pastDue.Sum(x => x.Count),
            ExpiringToday = expiringToday
        };

        summary.Active = summary.Reserved + summary.Pending + summary.Approved + summary.ReadyForPickup;
        summary.AwaitingAction = summary.Reserved + summary.Pending + summary.Approved;

        return summary;
    }

    public async Task<LibraryResult<ReservationDetailsDto>> GetReservationAsync(int id, CancellationToken cancellationToken)
    {
        var row = await Rows(_db.Reservations.AsNoTracking().Where(r => r.ReservationId == id))
            .FirstOrDefaultAsync(cancellationToken);

        if (row == null)
            return LibraryResult<ReservationDetailsDto>.Failure(404, "Reservation not found.");

        var extra = await _db.Reservations.AsNoTracking()
            .Where(r => r.ReservationId == id)
            .Select(r => new
            {
                r.Remarks,
                Isbn = r.Book != null ? r.Book.ISBN : null,
                CallNo = r.Book != null ? r.Book.CallNo : null,
                Shelf = r.Book != null ? r.Book.ShelfLocation : null,
                TotalCopies = r.Book != null ? r.Book.TotalCopies : 0
            })
            .FirstAsync(cancellationToken);

        var details = new ReservationDetailsDto();
        Fill(details, row, DateTime.Now);

        details.Remarks = extra.Remarks;
        details.ISBN = extra.Isbn;
        details.CallNo = extra.CallNo;
        details.ShelfLocation = extra.Shelf;
        details.TotalCopies = extra.TotalCopies;

        var patron = await FindPatronAsync(row.UserId, cancellationToken);

        if (patron == null)
        {
            details.CanBorrow = false;
            details.BorrowBlockReason = "This account is not a student or faculty patron.";
            details.CanMarkReady = false;
            details.CanClaim = false;

            return LibraryResult<ReservationDetailsDto>.Success(details);
        }

        var limit = LibraryDefaults.BorrowLimitFor(patron.Role);

        var activeLoans = await _db.BorrowTransactions.AsNoTracking()
            .CountAsync(t => t.UserId == patron.Id && t.ReturnDate == null, cancellationToken);

        // Same rules as the Borrow page (Part 7).
        var reason = AdminBorrowingService.CheckEligibility(patron, activeLoans, limit);

        details.PatronEmail = patron.Email;
        details.Course = patron.Course;
        details.YearLevel = patron.YearLevel;
        details.AccountStatus = patron.AccountStatus;
        details.BorrowLimit = limit;
        details.ActiveLoans = activeLoans;
        details.CanBorrow = reason == null;
        details.BorrowBlockReason = reason;

        // Do not offer actions that the server would refuse anyway.
        details.CanMarkReady = details.CanMarkReady && reason == null;
        details.CanClaim = details.CanClaim && reason == null;

        return LibraryResult<ReservationDetailsDto>.Success(details);
    }

    // =====================================================
    // ACTIONS
    //
    // read -> validate -> one transaction:
    //   1. change the status ONLY if it is still what we read (race-safe)
    //   2. move stock if needed (reservation row first, then the book row)
    //   3. notification + activity log
    // Returning before CommitAsync rolls everything back.
    // =====================================================

    private sealed record Snapshot(
        int ReservationId, int UserId, int BookId, string Status, DateTime ExpirationDate,
        string? Remarks, string BookTitle, string PatronName);

    private Task<Snapshot?> LoadAsync(int id, CancellationToken cancellationToken) =>
        _db.Reservations.AsNoTracking()
            .Where(r => r.ReservationId == id)
            .Select(r => new Snapshot(
                r.ReservationId,
                r.UserId,
                r.BookId,
                r.Status,
                r.ExpirationDate,
                r.Remarks,
                r.Book != null ? r.Book.Title : "(deleted book)",
                r.User != null ? r.User.FullName : "(deleted account)"))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<LibraryResult<ReservationListItemDto>> ApproveAsync(int id, int staffId, CancellationToken cancellationToken)
    {
        var res = await LoadAsync(id, cancellationToken);
        if (res == null) return Fail(404, "Reservation not found.");

        var now = DateTime.Now;

        var error = CheckOpen(res, now);
        if (error != null) return Fail(409, error);

        if (res.Status != ReservationStatuses.Pending)
            return Fail(409, "Only pending reservations need approval. Reservations made in the app are already accepted.");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        if (!await TransitionAsync(id, res.Status, ReservationStatuses.Approved, res.Remarks, null, cancellationToken))
            return Fail(409, ChangedByOthers);

        Notify(res.UserId, "reservation-approved", "Reservation Approved",
            $"Your reservation for '{res.BookTitle}' was approved. We will notify you when it is ready for pickup.", now);

        await PushNotifyAsync(res.UserId, id, "reservation-approved", "Reservation Approved",
            $"Your reservation for '{res.BookTitle}' was approved. We will notify you when it is ready for pickup.", cancellationToken);

        _activity.Add(staffId, "LIBRARY_RESERVATION_APPROVED",
            $"approved {res.PatronName}'s reservation of \"{res.BookTitle}\"");

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await ItemAsync(id, cancellationToken);
    }

    public async Task<LibraryResult<ReservationListItemDto>> MarkReadyAsync(int id, int staffId, CancellationToken cancellationToken)
    {
        var res = await LoadAsync(id, cancellationToken);
        if (res == null) return Fail(404, "Reservation not found.");

        var now = DateTime.Now;

        var error = CheckOpen(res, now);
        if (error != null) return Fail(409, error);

        if (!ReservationStatuses.AwaitingAction.Contains(res.Status))
            return Fail(409, $"This reservation is already {ReservationStatuses.Label(res.Status).ToLowerInvariant()}.");

        // Do not hold a copy for someone who cannot borrow it anyway.
        var patron = await FindPatronAsync(res.UserId, cancellationToken);
        if (patron == null) return Fail(404, "Patron not found.");

        var limit = LibraryDefaults.BorrowLimitFor(patron.Role);

        var activeLoans = await _db.BorrowTransactions.AsNoTracking()
            .CountAsync(t => t.UserId == patron.Id && t.ReturnDate == null, cancellationToken);

        var reason = AdminBorrowingService.CheckEligibility(patron, activeLoans, limit);
        if (reason != null)
            return Fail(409, $"{reason} Reject the reservation or wait until the patron can borrow.");

        if (await _db.BorrowTransactions.AnyAsync(
                t => t.UserId == patron.Id && t.BookId == res.BookId && t.ReturnDate == null, cancellationToken))
            return Fail(409, "This patron already has this book on loan.");

        var deadline = PickupDeadline(now);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        if (!await TransitionAsync(id, res.Status, ReservationStatuses.ReadyForPickup, res.Remarks, deadline, cancellationToken))
            return Fail(409, ChangedByOthers);

        if (!await LibraryStock.TryHoldCopyAsync(_db, res.BookId, now, cancellationToken))
            return Fail(409, "No copy of this book is on the shelf right now. Wait for a return, or reject the reservation.");

        Notify(res.UserId, "reservation-ready", "Ready for Pickup",
            $"'{res.BookTitle}' is ready for pickup at the library. Please claim it before {When(deadline)}.", now);

        await PushNotifyAsync(res.UserId, id, "reservation-ready", "Ready for Pickup",
            $"'{res.BookTitle}' is ready for pickup at the library. Please claim it before {When(deadline)}.", cancellationToken);

        _activity.Add(staffId, "LIBRARY_RESERVATION_READY",
            $"set aside \"{res.BookTitle}\" for {res.PatronName}");

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await ItemAsync(id, cancellationToken);
    }

    public async Task<LibraryResult<ReservationListItemDto>> RejectAsync(int id, string? reason, int staffId, CancellationToken cancellationToken)
    {
        var why = CleanReason(reason);

        if (why == null)
            return Fail(400, "Please enter the reason for rejecting this reservation.");

        if (why.Length > MaxReasonLength)
            return Fail(400, $"The reason must be {MaxReasonLength} characters or fewer.");

        var res = await LoadAsync(id, cancellationToken);
        if (res == null) return Fail(404, "Reservation not found.");

        var now = DateTime.Now;

        var error = CheckOpen(res, now);
        if (error != null) return Fail(409, error);

        if (!ReservationStatuses.AwaitingAction.Contains(res.Status))
            return Fail(409, "A copy is already set aside for this reservation. Use Cancel instead.");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        if (!await TransitionAsync(id, res.Status, ReservationStatuses.Rejected,
                AppendRemark(res.Remarks, $"Rejected: {why}"), null, cancellationToken))
            return Fail(409, ChangedByOthers);

        Notify(res.UserId, "reservation-rejected", "Reservation Rejected",
            $"Your reservation for '{res.BookTitle}' was rejected. Reason: {why}", now);

        await PushNotifyAsync(res.UserId, id, "reservation-rejected", "Reservation Rejected",
            $"Your reservation for '{res.BookTitle}' was rejected. Reason: {why}", cancellationToken);

        _activity.Add(staffId, "LIBRARY_RESERVATION_REJECTED",
            $"rejected {res.PatronName}'s reservation of \"{res.BookTitle}\"");

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await ItemAsync(id, cancellationToken);
    }

    public async Task<LibraryResult<ReservationListItemDto>> CancelAsync(int id, string? reason, int staffId, CancellationToken cancellationToken)
    {
        var why = CleanReason(reason);

        if (why != null && why.Length > MaxReasonLength)
            return Fail(400, $"The reason must be {MaxReasonLength} characters or fewer.");

        var res = await LoadAsync(id, cancellationToken);
        if (res == null) return Fail(404, "Reservation not found.");

        var now = DateTime.Now;

        var error = CheckOpen(res, now);
        if (error != null) return Fail(409, error);

        var note = why == null ? "Cancelled by the library" : $"Cancelled by the library: {why}";

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        if (!await TransitionAsync(id, res.Status, ReservationStatuses.Cancelled,
                AppendRemark(res.Remarks, note), null, cancellationToken))
            return Fail(409, ChangedByOthers);

        // A ReadyForPickup reservation was holding a copy: put it back on the shelf.
        if (ReservationStatuses.HoldsCopy(res.Status))
            await LibraryStock.ReleaseCopyAsync(_db, res.BookId, now, cancellationToken);

        Notify(res.UserId, "reservation-cancelled", "Reservation Cancelled",
            why == null
                ? $"The library cancelled your reservation for '{res.BookTitle}'."
                : $"The library cancelled your reservation for '{res.BookTitle}'. Reason: {why}", now);

        await PushNotifyAsync(res.UserId, id, "reservation-cancelled", "Reservation Cancelled",
            why == null
                ? $"The library cancelled your reservation for '{res.BookTitle}'."
                : $"The library cancelled your reservation for '{res.BookTitle}'. Reason: {why}", cancellationToken);

        _activity.Add(staffId, "LIBRARY_RESERVATION_CANCELLED",
            $"cancelled {res.PatronName}'s reservation of \"{res.BookTitle}\"");

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await ItemAsync(id, cancellationToken);
    }

    // =====================================================
    // CLAIM = convert to a loan (one database transaction)
    // =====================================================

    public async Task<LibraryResult<BorrowResponseDto>> ClaimAsync(int id, ClaimReservationDto dto, int staffId, CancellationToken cancellationToken)
    {
        var loanDays = dto.LoanDays ?? LibraryDefaults.DefaultLoanDays;

        if (!LibraryDefaults.LoanDayOptions.Contains(loanDays))
            return ClaimFail(400, $"Choose a loan period of {string.Join(", ", LibraryDefaults.LoanDayOptions)} days.");

        if (!dto.AcceptedTerms)
            return ClaimFail(400, "The borrowing terms must be accepted.");

        var res = await LoadAsync(id, cancellationToken);
        if (res == null) return ClaimFail(404, "Reservation not found.");

        var now = DateTime.Now;

        var error = CheckOpen(res, now);
        if (error != null) return ClaimFail(409, error);

        if (res.Status == ReservationStatuses.Pending)
            return ClaimFail(409, "This reservation still needs the Library Head's approval.");

        if (!ReservationStatuses.IsClaimable(res.Status))
            return ClaimFail(409, $"This reservation cannot be claimed (status: {ReservationStatuses.Label(res.Status)}).");

        var patron = await FindPatronAsync(res.UserId, cancellationToken);
        if (patron == null) return ClaimFail(404, "Patron not found.");

        var book = await _db.Books.AsNoTracking()
            .Where(b => b.BookId == res.BookId)
            .Select(b => new { b.BookId, b.Title, b.Author, b.ISBN, b.CallNo })
            .FirstOrDefaultAsync(cancellationToken);

        if (book == null) return ClaimFail(404, "Book not found.");

        // Anything that returns or throws after this point rolls the transaction back.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var limit = LibraryDefaults.BorrowLimitFor(patron.Role);

        var activeLoans = await _db.BorrowTransactions.AsNoTracking()
            .CountAsync(t => t.UserId == patron.Id && t.ReturnDate == null, cancellationToken);

        var reason = AdminBorrowingService.CheckEligibility(patron, activeLoans, limit);
        if (reason != null) return ClaimFail(409, reason);

        if (await _db.BorrowTransactions.AnyAsync(
                t => t.UserId == patron.Id && t.BookId == book.BookId && t.ReturnDate == null, cancellationToken))
            return ClaimFail(409, "This patron already has this book on loan.");

        // Only one request can win this. The loser gets 409 and nothing is saved.
        if (!await TransitionAsync(id, res.Status, ReservationStatuses.Claimed, res.Remarks, null, cancellationToken))
            return ClaimFail(409, ChangedByOthers);

        // ReadyForPickup already holds its copy. Any other status takes one now.
        if (!ReservationStatuses.HoldsCopy(res.Status) &&
            !await LibraryStock.TryHoldCopyAsync(_db, book.BookId, now, cancellationToken))
            return ClaimFail(409, "No copy of this book is available right now.");

        var due = AdminBorrowingService.DueDateFor(now, loanDays);

        var loan = new BorrowTransaction
        {
            UserId = patron.Id,
            BookId = book.BookId,
            BorrowDate = now,
            DueDate = due,
            Status = "Borrowed",
            RenewalCount = 0,
            Remarks = $"From reservation #{res.ReservationId}",
            CreatedAt = now
        };

        _db.BorrowTransactions.Add(loan);

        // Same student-side records the old claim endpoint wrote, so the mobile app stays consistent.
        _db.LibraryActivities.Add(new LibraryActivity
        {
            UserId = patron.Id,
            BookId = book.BookId,
            ActivityType = "ClaimReservation",
            Title = "Book Claimed",
            Description = $"You claimed '{book.Title}'.",
            CreatedAt = now
        });

        Notify(patron.Id, "reservation-claimed", "Book Claimed",
            $"You claimed your reserved book '{book.Title}'. Please return it by {Day(due)}.", now);

        await PushNotifyAsync(patron.Id, id, "reservation-claimed", "Book Claimed",
            $"You claimed your reserved book '{book.Title}'. Please return it by {Day(due)}.", cancellationToken);

        _activity.Add(staffId, "LIBRARY_RESERVATION_CLAIMED",
            $"issued reserved book \"{book.Title}\" to {patron.FullName}");

        await _db.SaveChangesAsync(cancellationToken);

        // Link the reservation to the new loan. BorrowId exists only after SaveChanges.
        var linkedRemarks = AppendRemark(res.Remarks, $"Claimed as loan #{loan.BorrowId}");

        await _db.Reservations
            .Where(x => x.ReservationId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Remarks, linkedRemarks), cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var staffName = await _db.Users.AsNoTracking()
            .Where(u => u.Id == staffId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Library staff";

        // Same shape as the Part 7 receipt, so the frontend can reuse it.
        return LibraryResult<BorrowResponseDto>.Success(new BorrowResponseDto
        {
            TransactionId = loan.BorrowId,
            UserId = patron.Id,
            PatronName = patron.FullName,
            PatronId = patron.IdNumber,
            PatronType = patron.Role,
            BookId = book.BookId,
            BookTitle = book.Title,
            Author = book.Author,
            ISBN = book.ISBN,
            CallNo = book.CallNo,
            BorrowDate = now,
            DueDate = due,
            LoanDays = loanDays,
            RemainingSlots = Math.Max(0, limit - (activeLoans + 1)),
            ProcessedBy = staffName
        }, 201);
    }

    // =====================================================
    // EXPIRY (called by ReservationExpiryBackgroundService)
    // =====================================================

    public async Task<int> ExpireOverdueAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.Now;

        var overdue = await _db.Reservations.AsNoTracking()
            .Where(r => ReservationStatuses.Active.Contains(r.Status) && r.ExpirationDate < now)
            .OrderBy(r => r.ExpirationDate)
            .Take(100)
            .Select(r => new
            {
                r.ReservationId,
                r.UserId,
                r.BookId,
                r.Status,
                r.Remarks,
                BookTitle = r.Book != null ? r.Book.Title : "a book"
            })
            .ToListAsync(cancellationToken);

        var expired = 0;

        foreach (var item in overdue)
        {
            // One small transaction per reservation.
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            // Skip it if the patron, the scanner or the Head changed it in the meantime.
            if (!await TransitionAsync(item.ReservationId, item.Status, ReservationStatuses.Expired,
                    item.Remarks, null, cancellationToken))
                continue;

            if (ReservationStatuses.HoldsCopy(item.Status))
                await LibraryStock.ReleaseCopyAsync(_db, item.BookId, now, cancellationToken);

            Notify(item.UserId, "reservation-expired", "Reservation Expired",
                $"Your reservation for '{item.BookTitle}' has expired.", now);

            await PushNotifyAsync(item.UserId, item.ReservationId, "reservation-expired", "Reservation Expired",
                $"Your reservation for '{item.BookTitle}' has expired.", cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            expired++;
        }

        return expired;
    }

    // =====================================================
    // HELPERS
    // =====================================================

    // UPDATE reservations SET Status = @to ... WHERE ReservationId = @id AND Status = @from
    // 0 rows changed = another request changed it first.
    private async Task<bool> TransitionAsync(
        int id, string from, string to, string? remarks, DateTime? newExpiration, CancellationToken cancellationToken)
    {
        var query = _db.Reservations.Where(r => r.ReservationId == id && r.Status == from);

        var changed = newExpiration is DateTime expiration
            ? await query.ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, to)
                .SetProperty(r => r.Remarks, remarks)
                .SetProperty(r => r.ExpirationDate, expiration), cancellationToken)
            : await query.ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, to)
                .SetProperty(r => r.Remarks, remarks), cancellationToken);

        return changed == 1;
    }

    // Returns an error message, or null when the reservation is still open.
    private static string? CheckOpen(Snapshot res, DateTime now)
    {
        if (!ReservationStatuses.IsActive(res.Status))
            return $"This reservation is already {ReservationStatuses.Label(res.Status).ToLowerInvariant()}.";

        if (res.ExpirationDate < now)
            return "This reservation has already expired.";

        return null;
    }

    // Only the fields the eligibility check needs. Never loads PasswordHash.
    private Task<User?> FindPatronAsync(int userId, CancellationToken cancellationToken) =>
        _db.Users.AsNoTracking()
            .Where(u => u.Id == userId && PatronRoles.Contains(u.Role))
            .Select(u => new User
            {
                Id = u.Id,
                IdNumber = u.IdNumber,
                FullName = u.FullName,
                Email = u.Email,
                Role = u.Role,
                Institute = u.Institute,
                Course = u.Course,
                YearLevel = u.YearLevel,
                IsVerified = u.IsVerified,
                IsProfileComplete = u.IsProfileComplete,
                AccountStatus = u.AccountStatus,
                BorrowBanUntil = u.BorrowBanUntil
            })
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<LibraryResult<ReservationListItemDto>> ItemAsync(int id, CancellationToken cancellationToken)
    {
        var row = await Rows(_db.Reservations.AsNoTracking().Where(r => r.ReservationId == id))
            .FirstOrDefaultAsync(cancellationToken);

        return row == null
            ? Fail(404, "Reservation not found.")
            : LibraryResult<ReservationListItemDto>.Success(ToItem(row, DateTime.Now));
    }

    private void Notify(int userId, string type, string title, string message, DateTime now) =>
        _db.LibraryNotifications.Add(new LibraryNotification
        {
            UserId = userId,
            Type = type,
            Title = title,
            Message = message,
            IsRead = false,
            CreatedAt = now
        });

    /// <summary>
    /// Same event, same words, delivered to the browser through the shared
    /// push outbox. Best-effort: a queue failure never fails the action.
    /// </summary>
    private async Task PushNotifyAsync(
        int userId,
        int reservationId,
        string type,
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await _push.EnqueueAsync(new PushNotification(
                UserId: userId,
                Module: "Library",
                Type: type,
                Title: title,
                Body: message,
                TargetUrl: PushRoutes.Resolve("Library", type),
                DedupeKey: $"{type}:{reservationId}"), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The in-app LibraryNotification row is already staged; push is
            // an extra, never a requirement.
        }
    }

    // The patron has until the end of the last pickup day.
    private static DateTime PickupDeadline(DateTime now) =>
        now.Date.AddDays(LibraryDefaults.ReservationPickupDays + 1).AddSeconds(-1);

    private static string AppendRemark(string? existing, string note) =>
        string.IsNullOrWhiteSpace(existing) ? note : $"{existing.Trim()} | {note}";

    private static string? CleanReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    private static string When(DateTime value) =>
        value.ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);

    private static string Day(DateTime value) =>
        value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    private static LibraryResult<ReservationListItemDto> Fail(int statusCode, string message) =>
        LibraryResult<ReservationListItemDto>.Failure(statusCode, message);

    private static LibraryResult<BorrowResponseDto> ClaimFail(int statusCode, string message) =>
        LibraryResult<BorrowResponseDto>.Failure(statusCode, message);
}