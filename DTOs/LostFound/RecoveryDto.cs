namespace CDM_OneServe_API.DTOs.LostFound;

public class RecoveryDto
{
    public int RecoveryId { get; set; }

    public int ReportId { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public int FinderUserId { get; set; }

    public string FinderName { get; set; } = string.Empty;

    public string FinderEmail { get; set; } = string.Empty;

    public string RecoveryMethod { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string ContactMethod { get; set; } = string.Empty;

    public string ContactInfo { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}