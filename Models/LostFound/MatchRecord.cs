using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.Models.LostFound;

public class MatchRecord
{
    [Key]
    public int MatchRecordId { get; set; }

    public int LostReportId { get; set; }

    public int FoundReportId { get; set; }

    // =========================================================
    // MATCH PERCENTAGE
    // =========================================================
    //
    // 100 = very strong match
    // 50  = partial match
    // 10  = low possible match
    //
    // There is NO minimum percentage required.
    // =========================================================

    public int MatchPercentage { get; set; }

    public bool IsConfirmed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public LostFoundReport? LostReport { get; set; }

    public LostFoundReport? FoundReport { get; set; }
}