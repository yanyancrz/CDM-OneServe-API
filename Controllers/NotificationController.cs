using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationController : ControllerBase
{
    private readonly AppDbContext _context;

    public NotificationController(AppDbContext context)
    {
        _context = context;
    }

    // GET: /api/notifications/{email}
    [HttpGet("{email}")]
    public async Task<IActionResult> GetNotifications(string email)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        // The {email} segment is retained for older clients. The JWT is
        // the only source of the account whose notifications are read.
        var rows = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(20)
            .Select(n => new
            {
                id = n.Id,
                title = n.Title,
                message = n.Message,
                type = n.Type,
                isRead = n.IsRead,
                createdAt = n.CreatedAt
            })
            .ToListAsync();

        // This table is shared. Guidance and Library rows are UTC wall clocks,
        // while the Lost & Found mirror copies that module's PH wall clocks.
        // Attach the right kind to each so the JSON carries an unambiguous
        // instant. No stored value is changed.
        var notifications = rows.Select(n => new
        {
            n.id,
            n.title,
            n.message,
            n.type,
            n.isRead,
            createdAt = n.type.StartsWith("LOSTFOUND_", StringComparison.OrdinalIgnoreCase)
                ? UtcTimestamps.FromPhWallClock(n.createdAt)
                : UtcTimestamps.FromDatabase(n.createdAt)
        });

        return Ok(notifications);
    }

    // GET unread count
    // GET: /api/notifications/{email}/unread-count
    [HttpGet("{email}/unread-count")]
    public async Task<IActionResult> GetUnreadCount(string email)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var count = await _context.Notifications
            .CountAsync(n =>
                n.UserId == userId &&
                !n.IsRead
            );

        return Ok(new
        {
            count
        });
    }

    // Mark one notification as read
    // PUT: /api/notifications/{id}/read?email=...
    [HttpPut("{id:int}/read")]
    public async Task<IActionResult> MarkAsRead(
        int id,
        [FromQuery] string? email = null)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n =>
                n.Id == id &&
                n.UserId == userId
            );

        if (notification == null)
        {
            return NotFound(new
            {
                message = "Notification not found."
            });
        }

        notification.IsRead = true;

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Notification marked as read."
        });
    }

    // Mark all notifications as read
    // PUT: /api/notifications/mark-all-read?email=...
    [HttpPut("mark-all-read")]
    public async Task<IActionResult> MarkAllAsRead(
        [FromQuery] string? email = null)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var notifications = await _context.Notifications
            .Where(n =>
                n.UserId == userId &&
                !n.IsRead
            )
            .ToListAsync();

        foreach (var notification in notifications)
        {
            notification.IsRead = true;
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "All notifications marked as read."
        });
    }
}