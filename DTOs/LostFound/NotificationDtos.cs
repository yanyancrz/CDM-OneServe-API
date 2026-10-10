namespace CDM_OneServe_API.DTOs.LostFound;

// =============================================================
// NOTIFICATION DTO
//
// The Lost & Found push-subscription DTO used to live here. Web Push is
// now portal-wide, so it moved to
// CDM_OneServe_API.DTOs.Notifications.PushSubscriptionDto and the
// subscription store it writes to is shared with every other module.
// =============================================================

public class NotificationDto
{
    public int NotificationId { get; set; }
    public int UserId { get; set; }
    public int? ReportId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string NotificationType { get; set; } = "General";
    public string? TargetUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
