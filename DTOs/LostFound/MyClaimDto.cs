namespace CDM_OneServe_API.DTOs.LostFound;

public class MyClaimDto
{
    public int ClaimId { get; set; }

    public int ReportId { get; set; }

    public string? ImagePath { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public string ReportType { get; set; } = string.Empty;

    public DateTime ClaimDate { get; set; }

    public string Status { get; set; } = string.Empty;

    public string VerificationNotes { get; set; } = string.Empty;

    public string ItemCustody { get; set; } = "None";

    // ---- what the claimant supplied ----

    public string? OwnershipExplanation { get; set; }

    public string? IdentifyingDetails { get; set; }

    public string? NoProofReason { get; set; }

    public bool HasProof { get; set; }

    public string? ProofFileName { get; set; }

    public string? ProofContentType { get; set; }

    // A proof is only fetched through the authorized endpoint, never
    // through a URL attached to a public listing.
    public string? ProofUrl { get; set; }

    // ---- the reviewer's answer, if any ----

    public string? DecisionReason { get; set; }

    public DateTime? DecisionAt { get; set; }
}
