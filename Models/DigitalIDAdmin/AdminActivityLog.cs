using System.ComponentModel.DataAnnotations.Schema;

namespace CDM_OneServe_API.Models.DigitalIDAdmin;

[Table("admin_activity_logs")]
public class AdminActivityLog
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Action { get; set; } = "";

    public DateTime Timestamp { get; set; } = DateTime.Now;
}