using CDM_OneServe_API.Data;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Notifications;

/// <summary>
/// Writes browser-notification jobs into <c>PushOutbox</c>.
///
/// This service NEVER contacts a push service and never holds a VAPID
/// key. Delivery belongs to the separate Node notification service, so
/// a browser-push outage can never turn "book a reservation" into a 500.
///
/// Call it inside the same unit of work as the event it reports, so a
/// rollback also discards the queued notification. Where the caller runs
/// an explicit transaction (Library reservations, admin returns), the
/// insert rides that transaction. Where it does not, the row commits with
/// its own statement - which at worst delivers a notification for an
/// action that was rolled back afterwards, never the other way round.
/// </summary>
public interface IPushDispatcher
{
    /// <summary>
    /// Queues a notification for every active browser subscription of
    /// <paramref name="notification"/>.UserId. No-ops when the user has
    /// none, so the outbox only ever holds work that will be delivered.
    /// </summary>
    Task<bool> EnqueueAsync(PushNotification notification, CancellationToken ct = default);

    /// <summary>
    /// Queues one notification per user that owns an active subscription.
    /// For campus-wide events (announcements) that are not addressed to a
    /// single user, so <see cref="PushNotification.UserId"/> is ignored.
    /// Returns the number of rows MySQL reported; 0 when nobody is
    /// subscribed, so no dead work reaches the outbox.
    /// </summary>
    Task<int> EnqueueBroadcastAsync(PushNotification notification, CancellationToken ct = default);

    /// <summary>
    /// Saves anything the dispatcher queued but the caller has not
    /// flushed yet (used by code paths that do not already save).
    /// </summary>
    Task FlushAsync(CancellationToken ct = default);

    /// <summary>Queues a self-test notification to a user's browsers.</summary>
    Task<bool> EnqueueTestAsync(int userId, int? pushSubscriptionId = null, CancellationToken ct = default);
}

public class PushDispatcher : IPushDispatcher
{
    private readonly AppDbContext _db;

    public PushDispatcher(AppDbContext db) => _db = db;

    public async Task<bool> EnqueueAsync(PushNotification notification, CancellationToken ct = default)
    {
        if (notification is null || notification.UserId <= 0) return false;

        // Only queue work we can actually deliver.
        var recipient = await ResolveRecipientAsync(notification, ct);
        if (recipient is null) return false;

        var (title, body) = PushRoutes.ShapeTitleAndBody(notification.Title, notification.Body);

        var targetUrl = PushRoutes.Resolve(
            notification.Module, notification.Type, notification.TargetUrl);

        // Daily bucket: the same event can only notify once per day, no
        // matter how many times a handler retries or a user double-taps.
        // "Remind once" events opt out and are capped at one delivery ever.
        var dedupeKey = notification.OnceOnly
            ? $"{notification.Module.ToLowerInvariant()}:{notification.DedupeKey}"
            : $"{notification.Module.ToLowerInvariant()}:{notification.DedupeKey}:{DateTime.UtcNow:yyyyMMdd}";

        if (dedupeKey.Length > 190) dedupeKey = dedupeKey[..190];

        // Cheap pre-check; the UNIQUE index is the hard guarantee below.
        var alreadyQueued = await _db.PushOutbox.AnyAsync(m => m.DedupeKey == dedupeKey, ct);
        if (alreadyQueued) return false;

        await _db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO PushOutbox
                (UserId, PushSubscriptionId, Module, Type, Title, Body,
                 TargetUrl, Tag, DedupeKey, Priority, Status, Attempts,
                 ScheduledAt, CreatedAt)
            VALUES
                ({0}, {1}, {2}, {3}, {4}, {5},
                 {6}, {7}, {8}, {9}, 'Pending', 0,
                 {10}, {11})
            ON DUPLICATE KEY UPDATE PushOutboxId = PushOutboxId
            """,
            [
                notification.UserId,
                notification.PushSubscriptionId,
                notification.Module,
                notification.Type,
                title,
                body,
                targetUrl,
                notification.Tag,
                dedupeKey,
                notification.Priority,
                DateTime.UtcNow,
                DateTime.UtcNow
            ],
            ct);

        return true;
    }

    public Task FlushAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    /// <summary>
    /// Broadcast fan-out. Runs as one INSERT ... SELECT over
    /// PushSubscriptions so a publish reaches every subscribed device
    /// without a loop of single-row round-trips, and the unique
    /// DedupeKey index still guarantees one delivery per user per day.
    ///
    /// PushSubscriptionId is left NULL on purpose: the worker then
    /// delivers the row to every device that user owns, instead of the
    /// single subscription the row was generated from.
    /// </summary>
    public async Task<int> EnqueueBroadcastAsync(
        PushNotification notification,
        CancellationToken ct = default)
    {
        if (notification is null) return 0;

        var (title, body) = PushRoutes.ShapeTitleAndBody(notification.Title, notification.Body);

        var targetUrl = PushRoutes.Resolve(
            notification.Module, notification.Type, notification.TargetUrl);

        // The user id is appended by SQL, so the key stays unique per
        // recipient while one statement covers everybody.
        var keyPrefix =
            $"{notification.Module.ToLowerInvariant()}:{notification.DedupeKey}:{DateTime.UtcNow:yyyyMMdd}:";
        if (keyPrefix.Length > 190) keyPrefix = keyPrefix[..190];

        return await _db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO PushOutbox
                (UserId, PushSubscriptionId, Module, Type, Title, Body,
                 TargetUrl, Tag, DedupeKey, Priority, Status, Attempts,
                 ScheduledAt, CreatedAt)
            SELECT
                s.UserId, NULL, {0}, {1}, {2}, {3},
                {4}, {5}, CONCAT({6}, s.UserId), {7}, 'Pending', 0,
                {8}, {8}
            FROM (SELECT DISTINCT UserId FROM PushSubscriptions WHERE IsActive = 1) s
            ON DUPLICATE KEY UPDATE PushOutboxId = PushOutboxId
            """,
            [
                notification.Module,
                notification.Type,
                title,
                body,
                targetUrl,
                notification.Tag,
                keyPrefix,
                notification.Priority,
                DateTime.UtcNow
            ],
            ct);
    }

    public Task<bool> EnqueueTestAsync(
        int userId,
        int? pushSubscriptionId = null,
        CancellationToken ct = default)
    {
        var notification = new PushNotification(
            UserId: userId,
            Module: "System",
            Type: "Test",
            Title: "CDM OneServe notifications are on",
            Body: "This is a test. Campus updates will appear here, even when the app is closed.",
            TargetUrl: "/dashboard",
            DedupeKey: $"test:{pushSubscriptionId?.ToString() ?? "all"}:{Guid.NewGuid():N}",
            Tag: "oneserve-test",
            Priority: 1,
            PushSubscriptionId: pushSubscriptionId);

        return EnqueueAsync(notification, ct);
    }

    /// <summary>
    /// Returns the recipient when there is a live subscription to deliver
    /// to (a specific one, or any one of the user's), otherwise null so
    /// no dead row is written.
    /// </summary>
    private async Task<int?> ResolveRecipientAsync(PushNotification notification, CancellationToken ct)
    {
        var query = _db.PushSubscriptions.AsNoTracking().Where(s => s.IsActive);

        if (notification.PushSubscriptionId is int single)
        {
            var owner = await query
                .Where(s => s.PushSubscriptionId == single)
                .Select(s => (int?)s.UserId)
                .FirstOrDefaultAsync(ct);

            return owner;
        }

        return await query
            .AnyAsync(s => s.UserId == notification.UserId, ct)
                ? notification.UserId
                : null;
    }
}
