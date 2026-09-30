using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CDM_OneServe_API.Models.Library;

[Table("library_attendance")]
public class LibraryAttendance
{
    [Key]
    public int AttendanceId { get; set; }

    public int UserId { get; set; }

    [MaxLength(50)]
    public string StudentNumber { get; set; } = string.Empty;

    [MaxLength(255)]
    public string StudentName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Institute { get; set; } = string.Empty;

    [MaxLength(255)]
    public string Purpose { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }

    public DateTime CreatedAt { get; set; }
}