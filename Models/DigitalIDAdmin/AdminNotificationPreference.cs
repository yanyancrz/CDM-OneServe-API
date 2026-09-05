using System.ComponentModel.DataAnnotations.Schema;

namespace CDM_OneServe_API.Models.DigitalIDAdmin;

[Table("admin_notification_preferences")]
public class AdminNotificationPreference
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public bool EmailOnNewRequest { get; set; } = true;

    public bool EmailOnThreshold { get; set; } = false;

    public int ThresholdCount { get; set; } = 10;
}