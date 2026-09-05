namespace CDM_OneServe_API.DTOs.LostFound;

public class CreateLostFoundItemDto
{
    public int UserId { get; set; }

    public string ItemName { get; set; } = "";

    public string Category { get; set; } = "";

    public string? Description { get; set; }

    public string ReportType { get; set; } = "";

    public DateTime DateLostFound { get; set; }

    public string Location { get; set; } = "";

    public string? Photo { get; set; }
}