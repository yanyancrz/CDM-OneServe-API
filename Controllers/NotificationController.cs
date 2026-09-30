using CDM_OneServe_API.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers;

[ApiController]
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
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            return NotFound(new
            {
                message = "User not found."
            });
        }

        var notifications = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == user.Id)
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

        return Ok(notifications);
    }

    // GET unread count
    // GET: /api/notifications/{email}/unread-count
    [HttpGet("{email}/unread-count")]
    public async Task<IActionResult> GetUnreadCount(string email)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            return NotFound(new
            {
                message = "User not found."
            });
        }

        var count = await _context.Notifications
            .CountAsync(n =>
                n.UserId == user.Id &&
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
        [FromQuery] string email)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            return NotFound(new
            {
                message = "User not found."
            });
        }

        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n =>
                n.Id == id &&
                n.UserId == user.Id
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
        [FromQuery] string email)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            return NotFound(new
            {
                message = "User not found."
            });
        }

        var notifications = await _context.Notifications
            .Where(n =>
                n.UserId == user.Id &&
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