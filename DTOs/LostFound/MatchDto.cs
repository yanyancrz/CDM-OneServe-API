namespace CDM_OneServe_API.DTOs.LostFound;

public class MatchDto
{
    public int MatchRecordId { get; set; }

    public int LostReportId { get; set; }

    public int FoundReportId { get; set; }

    public string LostItemName { get; set; } = "";

    public string FoundItemName { get; set; } = "";

    public string Location { get; set; } = "";

    public string? ImagePath { get; set; }

    public DateTime CreatedAt { get; set; }

    public bool IsConfirmed { get; set; }

    // =========================================================
    // MATCH PERCENTAGE (25 points per dimension: name, category,
    // location, date)
    // =========================================================

    public int MatchPercentage { get; set; }

    public int NameScore { get; set; }

    public int CategoryScore { get; set; }

    public int LocationScore { get; set; }

    public int DateScore { get; set; }
}