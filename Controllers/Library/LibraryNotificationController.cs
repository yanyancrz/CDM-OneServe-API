using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.Services.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers.Library
{
    [ApiController]
    [Authorize]
    [Route("api/library/notifications")]
    public class LibraryNotificationController : ControllerBase
    {
        private readonly LibraryNotificationService _notificationService;
        private readonly AppDbContext _context;

        public LibraryNotificationController(
            LibraryNotificationService notificationService,
            AppDbContext context)
        {
            _notificationService = notificationService;
            _context = context;
        }

        private bool CanAccessUser(int userId) =>
            User.TryGetUserId(out var currentUserId) &&
            (currentUserId == userId || User.IsLibraryStaff());


        // ============================
        // GET NOTIFICATIONS
        // GET: api/library/notifications/17
        // ============================
        [HttpGet("{userId}")]
        public async Task<IActionResult> GetNotifications(int userId)
        {
            if (!CanAccessUser(userId))
            {
                return Forbid();
            }

            var notifications =
                await _notificationService.GetNotificationsAsync(userId);

            return Ok(notifications);
        }


        // ============================
        // MARK ONE AS READ
        // PUT: api/library/notifications/read/5
        // ============================
        [HttpPut("read/{notificationId}")]
        public async Task<IActionResult> MarkAsRead(int notificationId)
        {
            if (!User.TryGetUserId(out var currentUserId))
            {
                return Unauthorized();
            }

            var notification = await _context.LibraryNotifications
                .AsNoTracking()
                .FirstOrDefaultAsync(n =>
                    n.NotificationId == notificationId);

            if (notification == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "Notification not found."
                });
            }

            if (notification.UserId != currentUserId &&
                !User.IsLibraryStaff())
            {
                return Forbid();
            }

            var success =
                await _notificationService.MarkAsReadAsync(notificationId);

            if (!success)
            {
                return NotFound(new
                {
                    success = false,
                    message = "Notification not found."
                });
            }

            return Ok(new
            {
                success = true,
                message = "Notification marked as read."
            });
        }


        // ============================
        // MARK ALL AS READ
        // PUT: api/library/notifications/read-all/17
        // ============================
        [HttpPut("read-all/{userId}")]
        public async Task<IActionResult> MarkAllAsRead(int userId)
        {
            if (!CanAccessUser(userId))
            {
                return Forbid();
            }

            var count =
                await _notificationService.MarkAllAsReadAsync(userId);

            return Ok(new
            {
                success = true,
                message = "Notifications marked as read.",
                totalUpdated = count
            });
        }


        // ============================
        // CLEAR ALL NOTIFICATIONS
        // DELETE: api/library/notifications/user/17/clear
        // ============================
        [HttpDelete("user/{userId}/clear")]
        public async Task<IActionResult> ClearNotifications(int userId)
        {
            if (!CanAccessUser(userId))
            {
                return Forbid();
            }

            var count =
                await _notificationService.ClearNotificationsAsync(userId);

            if (count == 0)
            {
                return Ok(new
                {
                    success = true,
                    message = "No notifications to clear.",
                    totalDeleted = 0
                });
            }

            return Ok(new
            {
                success = true,
                message = "Notifications cleared successfully.",
                totalDeleted = count
            });
        }
    }
}