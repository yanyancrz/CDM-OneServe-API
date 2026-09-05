namespace CDM_OneServe_API.DTOs.LostFound;

public class LostFoundClaimDto
{
    public int Id { get; set; }

    public int ItemId { get; set; }

    public int ClaimantUserId { get; set; }

    // Claim details
    public string? ClaimDescription { get; set; }

    public string Status { get; set; } = "Pending";

    public int? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public DateTime? ClaimedAt { get; set; }

    public DateTime CreatedAt { get; set; }


    // ==========================================
    // ITEM DETAILS
    // ==========================================

    public string? ItemName { get; set; }

    public string? Category { get; set; }

    public string? ReportType { get; set; }

    public string? Description { get; set; }

    public DateTime? DateLostFound { get; set; }

    public string? Location { get; set; }

    public string? Photo { get; set; }

    public string? ItemStatus { get; set; }

    public string? VerificationStatus { get; set; }
}