using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.Models.LostFound;

public class ClaimRecord
{
    [Key]
    public int ClaimId { get; set; }

    public int ReportId { get; set; }

    public LostFoundReport? Report { get; set; }

    public int ClaimedByUserId { get; set; }

    public DateTime ClaimDate { get; set; } = DateTime.UtcNow;

    public string VerificationNotes { get; set; } = string.Empty;

    public string Status { get; set; } = "Pending";
}