using CDM_OneServe_API.Services.Library;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Library
{
    [ApiController]
    [Route("api/library/notifications")]
    public class LibraryNotificationController : ControllerBase
    {
        private readonly LibraryNotificationService _notificationService;

        public LibraryNotificationController(
            LibraryNotificationService notificationService)
        {
            _notificationService = notificationService;
        }


        // ============================
        // GET NOTIFICATIONS
        // GET: api/library/notifications/17
        // ============================
        [HttpGet("{userId}")]
        public async Task<IActionResult> GetNotifications(int userId)
        {
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