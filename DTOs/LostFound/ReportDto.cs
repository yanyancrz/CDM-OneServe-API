namespace CDM_OneServe_API.DTOs.LostFound;

public class ReportDto
{
    public int ReportId { get; set; }
    public int UserId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime DateLostFound { get; set; }
    public string ReportType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ImagePath { get; set; }
    public string ItemCustody { get; set; } = "None";
    public DateTime CreatedAt { get; set; }
}
