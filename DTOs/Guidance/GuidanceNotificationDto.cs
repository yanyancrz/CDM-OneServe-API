namespace CDM_OneServe_API.DTOs.Guidance;

public class GuidanceNotificationDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    // GUIDANCE_APPOINTMENT_REQUEST | _CANCELLED | _CONFIRMED | _RESCHEDULED | _REJECTED
    public string Type { get; set; } = string.Empty;

    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class GuidanceNotificationListDto
{
    public List<GuidanceNotificationDto> Notifications { get; set; } = new();
    public int UnreadCount { get; set; }
}