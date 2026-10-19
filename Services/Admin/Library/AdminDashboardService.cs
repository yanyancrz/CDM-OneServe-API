using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin.Library.Dashboard;
using CDM_OneServe_API.Interfaces.Admin.Library;
using CDM_OneServe_API.Models.Library;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Library;

public class AdminDashboardService : IAdminDashboardService
{
    private readonly AppDbContext _db;

    public AdminDashboardService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<DashboardStatsDto> GetStatsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.Now;

        // Sequential on purpose: a DbContext cannot run queries in parallel.
        var totalBooks = await _db.Books.AsNoTracking().CountAsync(cancellationToken);

        var totalCopies = await _db.Books.AsNoTracking()
            .SumAsync(b => (int?)b.TotalCopies, cancellationToken) ?? 0;

        var activeBorrowed = await _db.BorrowTransactions.AsNoTracking()
            .CountAsync(t => t.ReturnDate == null, cancellationToken);

        var overdue = await _db.BorrowTransactions.AsNoTracking()
            .CountAsync(t => t.ReturnDate == null && t.DueDate < now, cancellationToken);

        // Reservations waiting for the library to act (not yet expired).
        var pending = await _db.Reservations.AsNoTracking()
            .CountAsync(r =>
                ReservationStatuses.AwaitingAction.Contains(r.Status) &&
                r.ExpirationDate >= now, cancellationToken);

        var students = await _db.Users.AsNoTracking()
            .CountAsync(u => u.Role == "Student", cancellationToken);

        var faculty = await _db.Users.AsNoTracking()
            .CountAsync(u => u.Role == "Faculty", cancellationToken);

        return new DashboardStatsDto
        {
            TotalCatalogBooks = totalBooks,
            TotalPhysicalCopies = totalCopies,
            ActiveBorrowedBooks = activeBorrowed,
            OverdueBooks = overdue,
            PendingReservations = pending,
            StudentPatrons = students,
            FacultyPatrons = faculty
        };
    }

    public async Task<List<OverdueBookDto>> GetOverdueAsync(int limit, CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 50);

        var now = DateTime.Now;

        var rows = await _db.BorrowTransactions.AsNoTracking()
            .Where(t => t.ReturnDate == null && t.DueDate < now)
            .OrderBy(t => t.DueDate)
            .Take(limit)
            .Select(t => new
            {
                TransactionId = t.BorrowId,
                PatronName = t.User!.FullName,
                PatronId = t.User!.IdNumber,
                PatronType = t.User!.Role,
                BookTitle = t.Book!.Title,
                t.BorrowDate,
                t.DueDate
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r =>
        {
            var overdueDays = Math.Max((now.Date - r.DueDate.Date).Days, 0);

            return new OverdueBookDto
            {
                TransactionId = r.TransactionId,
                PatronName = r.PatronName ?? string.Empty,
                PatronId = r.PatronId ?? string.Empty,
                PatronType = r.PatronType ?? string.Empty,
                BookTitle = r.BookTitle ?? string.Empty,
                BorrowDate = r.BorrowDate,
                DueDate = r.DueDate,
                OverdueDays = overdueDays
            };
        }).ToList();
    }

    public async Task<List<RecentActivityDto>> GetRecentActivityAsync(
    int limit, int currentUserId, bool allStaff, CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 50);

        var logs = _db.AdminActivityLogs.AsNoTracking()
            .Where(l => l.Action.StartsWith("LIBRARY_"));

        if (!allStaff)
            logs = logs.Where(l => l.UserId == currentUserId);

        var rows = await (
            from log in logs
            join user in _db.Users.AsNoTracking() on log.UserId equals user.Id into g
            from user in g.DefaultIfEmpty()
            orderby log.Timestamp descending
            select new
            {
                log.Id,
                log.UserId,
                ActorName = user != null ? user.FullName : null,
                log.Action,
                log.Timestamp
            })
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(r =>
        {
            var raw = r.Action ?? string.Empty;
            var idx = raw.IndexOf(':');

            return new RecentActivityDto
            {
                Id = r.Id,
                UserId = r.UserId,
                ActorName = r.ActorName ?? "Library staff",
                IsMine = r.UserId == currentUserId,
                Code = idx > 0 ? raw[..idx].Trim() : raw.Trim(),
                Message = idx > 0 ? raw[(idx + 1)..].Trim() : string.Empty,
                CreatedAt = r.Timestamp
            };
        }).ToList();
    }
}