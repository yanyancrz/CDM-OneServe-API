namespace CDM_OneServe_API.DTOs.LostFound;

public class LostFoundItemDto
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string? FullName { get; set; }

    public string? IdNumber { get; set; }

    public string ItemName { get; set; } = "";

    public string Category { get; set; } = "";

    public string? Description { get; set; }

    public string ReportType { get; set; } = "";

    public DateTime DateLostFound { get; set; }

    public string Location { get; set; } = "";

    public string? Photo { get; set; }

    public string Status { get; set; } = "";

    public string VerificationStatus { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}