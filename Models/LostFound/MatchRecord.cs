using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.Models.LostFound;

public class MatchRecord
{
    [Key]
    public int MatchRecordId { get; set; }

    public int LostReportId { get; set; }

    public int FoundReportId { get; set; }

    // =========================================================
    // MATCH PERCENTAGE (automatic, 25 points per dimension)
    // =========================================================
    //
    // Name = word overlap, Category = exact, Location = exact/contains,
    // Date = same day / within 3 / within 7 days. The four breakdown
    // columns below make every total explainable.
    // =========================================================

    public int MatchPercentage { get; set; }

    public int NameScore { get; set; }

    public int CategoryScore { get; set; }

    public int LocationScore { get; set; }

    public int DateScore { get; set; }

    public bool IsConfirmed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public LostFoundReport? LostReport { get; set; }

    public LostFoundReport? FoundReport { get; set; }
}