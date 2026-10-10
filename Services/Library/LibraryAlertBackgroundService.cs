using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models.Library;
using CDM_OneServe_API.Services.Notifications;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CDM_OneServe_API.Services.Library;

/// <summary>
/// Runs once at startup, then every 6 hours:
///   - books due within 1 day -> "due-soon" notification
///   - books past their due date -> "overdue" notification
///   - reservations expiring within 1 day -> "expiring" notification
///
/// The AlreadyNotified table deduplicates: each book/reservation gets at
/// most one notification per state transition, so a user is not spammed
/// on every run.
/// </summary>
public class LibraryAlertBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LibraryAlertBackgroundService> _logger;

    public LibraryAlertBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<LibraryAlertBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // The app is shutting down.
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            // AppDbContext is scoped, so every run gets its own scope.
            await using var scope = _scopeFactory.CreateAsyncScope();

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var push = scope.ServiceProvider.GetRequiredService<IPushDispatcher>();

            var dueSoonCount = await NotifyDueSoonBooksAsync(db, push, stoppingToken);
            var overdueCount = await NotifyOverdueBooksAsync(db, push, stoppingToken);
            var expiringCount = await NotifyExpiringReservationsAsync(db, push, stoppingToken);

            if (dueSoonCount + overdueCount + expiringCount > 0)
                _logger.LogInformation(
                    "Library alerts: {DueSoon} due-soon, {Overdue} overdue, {Expiring} reservation-expiring.",
                    dueSoonCount,
                    overdueCount,
                    expiringCount
                );
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never crash the API because of this job. It tries again on the next tick.
            _logger.LogError(ex, "Library alert job failed.");
        }
    }

    // ---------------------------------------------------------
    // DUE SOON: active borrows whose DueDate is within 1 day
    // ---------------------------------------------------------

    private async Task<int> NotifyDueSoonBooksAsync(
        AppDbContext db,
        IPushDispatcher push,
        CancellationToken stoppingToken
    )
    {
        var now = DateTime.Now;
        var threshold = now.AddDays(1);

        var loans = await db.BorrowTransactions
            .Where(b => b.Status == "Borrowed" && b.DueDate <= threshold && b.DueDate > now)
            .ToListAsync(stoppingToken);

        if (loans.Count == 0)
            return 0;

        var count = 0;

        foreach (var loan in loans)
        {
            var bookTitle = await GetBookTitleAsync(db, loan.BookId, stoppingToken);
            var dueLabel = loan.DueDate.ToString("MMM dd, yyyy h:mm tt");

            // One reminder per book until it is returned, renewed, or cleared.
            var alreadyNotified = await db.LibraryNotifications.AnyAsync(
                n => n.Type == "due-soon"
                     && n.UserId == loan.UserId
                     && n.Message.Contains(bookTitle),
                stoppingToken);

            if (alreadyNotified)
                continue;

            db.LibraryNotifications.Add(new LibraryNotification
            {
                UserId = loan.UserId,
                Type = "due-soon",
                Title = "Book due soon",
                Message =
                    $"Your borrowed book \"{bookTitle}\" is due on {dueLabel}. Return or renew it to avoid a fine.",
                IsRead = false,
                CreatedAt = DateTime.Now,
            });

            await push.EnqueueAsync(new PushNotification(
                loan.UserId,
                "Library",
                "due-soon",
                "Book due soon",
                $"Your borrowed book \"{bookTitle}\" is due on {dueLabel}. Return or renew it to avoid a fine.",
                PushRoutes.Resolve("Library", "due-soon"),
                $"duesoon:{loan.BorrowId}",
                OnceOnly: true), stoppingToken);

            count++;
        }

        if (count > 0)
            await db.SaveChangesAsync(stoppingToken);

        return count;
    }

    // ---------------------------------------------------------
    // OVERDUE: active borrows past their DueDate
    // ---------------------------------------------------------

    private async Task<int> NotifyOverdueBooksAsync(
        AppDbContext db,
        IPushDispatcher push,
        CancellationToken stoppingToken
    )
    {
        var now = DateTime.Now;

        var loans = await db.BorrowTransactions
            .Where(b => b.Status == "Borrowed" && b.DueDate < now)
            .ToListAsync(stoppingToken);

        if (loans.Count == 0)
            return 0;

        var count = 0;

        foreach (var loan in loans)
        {
            var bookTitle = await GetBookTitleAsync(db, loan.BookId, stoppingToken);
            var dueLabel = loan.DueDate.ToString("MMM dd, yyyy");

            // One reminder per book until it is returned or cleared.
            var alreadyNotified = await db.LibraryNotifications.AnyAsync(
                n => n.Type == "overdue"
                     && n.UserId == loan.UserId
                     && n.Message.Contains(bookTitle),
                stoppingToken);

            if (alreadyNotified)
                continue;

            var overdueDays = (now - loan.DueDate).Days;
            var fine = overdueDays * 10;

            db.LibraryNotifications.Add(new LibraryNotification
            {
                UserId = loan.UserId,
                Type = "overdue",
                Title = "Book overdue",
                Message =
                    $"Your borrowed book \"{bookTitle}\" is {overdueDays} day(s) overdue (due {dueLabel}). Fine: PHP {fine}. Return it as soon as possible.",
                IsRead = false,
                CreatedAt = DateTime.Now,
            });

            await push.EnqueueAsync(new PushNotification(
                loan.UserId,
                "Library",
                "overdue",
                "Book overdue",
                $"Your borrowed book \"{bookTitle}\" is {overdueDays} day(s) overdue (due {dueLabel}). Fine: PHP {fine}. Return it as soon as possible.",
                PushRoutes.Resolve("Library", "overdue"),
                $"overdue:{loan.BorrowId}",
                OnceOnly: true), stoppingToken);

            count++;
        }

        if (count > 0)
            await db.SaveChangesAsync(stoppingToken);

        return count;
    }

    // ---------------------------------------------------------
    // EXPIRING RESERVATIONS: active reservations within 1 day
    // of their ExpirationDate
    // ---------------------------------------------------------

    private async Task<int> NotifyExpiringReservationsAsync(
        AppDbContext db,
        IPushDispatcher push,
        CancellationToken stoppingToken
    )
    {
        var now = DateTime.Now;
        var threshold = now.AddDays(1);

        // ReadyForPickup counts as active: the book is held at the
        // counter and the student still has to collect it.
        var activeStatuses = new[]
        {
            "Reserved",
            "Pending",
            "Approved",
            "ReadyForPickup",
        };

        var reservations = await db.Reservations
            .Where(r => activeStatuses.Contains(r.Status) && r.ExpirationDate <= threshold && r.ExpirationDate > now)
            .ToListAsync(stoppingToken);

        if (reservations.Count == 0)
            return 0;

        var count = 0;

        foreach (var reservation in reservations)
        {
            var bookTitle = await GetBookTitleAsync(db, reservation.BookId, stoppingToken);
            var expireLabel = reservation.ExpirationDate.ToString("MMM dd, yyyy h:mm tt");

            // Dedup per user + type + book title, so one reminder per
            // reservation until it is claimed, expired, or cleared.
            var alreadyNotified = await db.LibraryNotifications.AnyAsync(
                n => n.Type == "reservation-expiring"
                     && n.UserId == reservation.UserId
                     && n.Message.Contains(bookTitle),
                stoppingToken);

            if (alreadyNotified)
                continue;

            db.LibraryNotifications.Add(new LibraryNotification
            {
                UserId = reservation.UserId,
                Type = "reservation-expiring",
                Title = "Reservation expiring soon",
                Message =
                    $"Your reservation for \"{bookTitle}\" expires on {expireLabel}. Pick it up before then or it will be released.",
                IsRead = false,
                CreatedAt = DateTime.Now,
            });

            await push.EnqueueAsync(new PushNotification(
                reservation.UserId,
                "Library",
                "reservation-expiring",
                "Reservation expiring soon",
                $"Your reservation for \"{bookTitle}\" expires on {expireLabel}. Pick it up before then or it will be released.",
                PushRoutes.Resolve("Library", "reservation-expiring"),
                $"reservationexpiring:{reservation.ReservationId}",
                OnceOnly: true), stoppingToken);

            count++;
        }

        if (count > 0)
            await db.SaveChangesAsync(stoppingToken);

        return count;
    }

    private async Task<string> GetBookTitleAsync(
        AppDbContext db,
        int bookId,
        CancellationToken stoppingToken
    )
    {
        var book = await db.Books
            .Where(b => b.BookId == bookId)
            .Select(b => b.Title)
            .FirstOrDefaultAsync(stoppingToken);

        return book ?? $"Book #{bookId}";
    }
}
