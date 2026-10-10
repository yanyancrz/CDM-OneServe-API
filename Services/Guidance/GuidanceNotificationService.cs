using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Guidance;
using CDM_OneServe_API.Hubs;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Services.Notifications;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Guidance;

/// <summary>
/// The signed-in user's Guidance alerts. They live in the shared OneServe
/// Notifications table (so the OneServe bottom nav shows them too); this service
/// only ever reads or changes the rows of the JWT user whose Type starts with GUIDANCE_.
/// </summary>
public class GuidanceNotificationService
{
    private const string TypePrefix = "GUIDANCE_";
    private const int MaxItems = 30;

    private readonly AppDbContext _context;
    private readonly IHubContext<GuidanceHub> _hub;
    private readonly IPushDispatcher _push;

    public GuidanceNotificationService(
        AppDbContext context,
        IHubContext<GuidanceHub> hub,
        IPushDispatcher push)
    {
        _context = context;
        _hub = hub;
        _push = push;
    }

    // =========================================================
    // CREATE + PUSH
    // =========================================================

    /// <summary>
    /// Saves a Guidance notification row, pushes it live to the user's
    /// open Guidance tabs ("Notification" event on the "user:{id}" SignalR
    /// group) and queues a browser notification for when the app is closed.
    /// A push failure must never fail the notification itself.
    /// </summary>
    public async Task<Notification> CreateAsync(
        int userId, string type, string title, string message, CancellationToken ct)
    {
        var notification = new Notification
        {
            UserId = userId,
            Type = type,
            Title = title,
            Message = message
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync(ct);

        // The in-app row already exists, so the browser push is best-effort:
        // a queue failure only means "no tray notification", never "no alert".
        _ = await _push.EnqueueAsync(new PushNotification(
            UserId: userId,
            Module: "Guidance",
            Type: type,
            Title: title,
            Body: message,
            // Guidance targets depend on the recipient's role, but the
            // /guidance appointments list is correct for both.
            TargetUrl: PushRoutes.Resolve("Guidance", type),
            DedupeKey: $"guidance:{type}:{notification.Id}"), ct);

        try
        {
            await _hub.Clients.Group($"user:{userId}").SendAsync("Notification", new
            {
                notification.Id,
                notification.Title,
                notification.Message,
                notification.Type,
                notification.IsRead,
                notification.CreatedAt
            }, ct);
        }
        catch
        {
            // Real-time is best-effort; the row is already saved.
        }

        return notification;
    }

    public async Task<GuidanceNotificationListDto> GetAsync(int userId, CancellationToken ct)
    {
        var mine = _context.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId && n.Type.StartsWith(TypePrefix));

        var items = await mine
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(MaxItems)
            .Select(n => new GuidanceNotificationDto
            {
                Id = n.Id,
                Title = n.Title,
                Message = n.Message,
                Type = n.Type,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt
            })
            .ToListAsync(ct);

        return new GuidanceNotificationListDto
        {
            Notifications = items,
            UnreadCount = await mine.CountAsync(n => !n.IsRead, ct)
        };
    }

    // false = not found (also when it belongs to someone else).
    public async Task<bool> MarkReadAsync(int userId, int id, CancellationToken ct)
    {
        var notification = await _context.Notifications.FirstOrDefaultAsync(
            n => n.Id == id && n.UserId == userId && n.Type.StartsWith(TypePrefix), ct);

        if (notification == null) return false;

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await _context.SaveChangesAsync(ct);
        }

        return true;
    }

    public Task<int> MarkAllReadAsync(int userId, CancellationToken ct) =>
        _context.Notifications
            .Where(n => n.UserId == userId && n.Type.StartsWith(TypePrefix) && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
}