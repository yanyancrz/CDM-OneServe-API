using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.Models.LostFound;

public class RecoveryRecord
{
    [Key]
    public int RecoveryId { get; set; }

    public int ReportId { get; set; }

    public LostFoundReport? Report { get; set; }

    public int FinderUserId { get; set; }

    // Student or Admin
    public string RecoveryMethod { get; set; } = "Student";

    // Message/details supplied by the finder
    public string Message { get; set; } = string.Empty;

    // Messenger, Contact Number, Instagram
    public string ContactMethod { get; set; } = string.Empty;

    // Actual contact information supplied by the finder
    public string ContactInfo { get; set; } = string.Empty;

    // Pending, AtAdminOffice, Completed, Rejected
    public string Status { get; set; } = "Pending";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }
}