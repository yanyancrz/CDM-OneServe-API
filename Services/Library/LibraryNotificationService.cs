using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Library;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Library
{
    public class LibraryNotificationService
    {
        private readonly AppDbContext _context;

        public LibraryNotificationService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<LibraryNotificationDto>> GetNotificationsAsync(int userId)
        {
            return await _context.LibraryNotifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new LibraryNotificationDto
                {
                    Id = n.NotificationId,
                    Type = n.Type,
                    Title = n.Title,
                    Description = n.Message,
                    CreatedAt = n.CreatedAt,
                    Read = n.IsRead
                })
                .ToListAsync();
        }

        public async Task<bool> MarkAsReadAsync(int notificationId)
        {
            var notification = await _context.LibraryNotifications
                .FirstOrDefaultAsync(n => n.NotificationId == notificationId);

            if (notification == null)
                return false;

            notification.IsRead = true;

            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<int> MarkAllAsReadAsync(int userId)
        {
            var notifications = await _context.LibraryNotifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ToListAsync();

            if (notifications.Count == 0)
                return 0;

            foreach (var notification in notifications)
            {
                notification.IsRead = true;
            }

            await _context.SaveChangesAsync();

            return notifications.Count;
        }

        public async Task<int> ClearNotificationsAsync(int userId)
        {
            var notifications = await _context.LibraryNotifications
                .Where(n => n.UserId == userId)
                .ToListAsync();

            if (notifications.Count == 0)
            {
                return 0;
            }

            _context.LibraryNotifications.RemoveRange(notifications);

            await _context.SaveChangesAsync();

            return notifications.Count;
        }
    }
}