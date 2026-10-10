namespace CDM_OneServe_API.DTOs.LostFound;

public class CreateRecoveryDto
{
    public int ReportId { get; set; }

    // Student or Admin
    public string RecoveryMethod { get; set; } = "Student";

    public string Message { get; set; } = string.Empty;

    // Messenger, Contact Number, Instagram
    public string ContactMethod { get; set; } = string.Empty;

    // Actual contact information
    public string ContactInfo { get; set; } = string.Empty;
}