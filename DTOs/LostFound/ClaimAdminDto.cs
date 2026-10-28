namespace CDM_OneServe_API.DTOs.LostFound;

/** A claim as the finder / admin console sees it, with names resolved. */
public class ClaimAdminDto
{
    public int ClaimId { get; set; }

    public int ReportId { get; set; }

    public int ClaimedByUserId { get; set; }

    public string ClaimantName { get; set; } = string.Empty;

    public string ClaimantEmail { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public string ReportType { get; set; } = string.Empty;

    public string FinderName { get; set; } = string.Empty;

    public string FinderEmail { get; set; } = string.Empty;

    public string ItemCustody { get; set; } = "None";

    public DateTime ClaimDate { get; set; }

    public string VerificationNotes { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    // ---- the evidence the claimant supplied ----

    public string? OwnershipExplanation { get; set; }

    public string? IdentifyingDetails { get; set; }

    public string? NoProofReason { get; set; }

    public bool HasProof { get; set; }

    public string? ProofFileName { get; set; }

    public string? ProofContentType { get; set; }

    /// <summary>Authorized proof endpoint - reachable only by the reviewers.</summary>
    public string? ProofUrl { get; set; }

    // ---- the reviewer's answer, if any ----

    public string? DecisionReason { get; set; }

    public DateTime? DecisionAt { get; set; }

    /** Who decided (snapshot taken when the decision was made). */
    public int? DecisionByUserId { get; set; }

    public string? DecisionByName { get; set; }

    public string? DecisionByRole { get; set; }
}

/** The Lost & Found admin dashboard counters. */
public class AdminStatsDto
{
    public int TotalReports { get; set; }

    public int PendingReports { get; set; }

    public int PublishedReports { get; set; }

    public int RejectedReports { get; set; }

    public int ReturnedReports { get; set; }

    public int TotalClaims { get; set; }

    public int ProcessingClaims { get; set; }

    public int AwaitingPickupClaims { get; set; }

    public int RejectedClaims { get; set; }

    public int ReturnedClaims { get; set; }

    public int TotalSchedules { get; set; }

    public int ScheduledPickups { get; set; }

    public int CompletedPickups { get; set; }

    public int TotalMatches { get; set; }

    public int ConfirmedMatches { get; set; }

    public int TotalRecovery { get; set; }

    public int PendingRecovery { get; set; }

    public int AtAdminOfficeRecovery { get; set; }

    public int CompletedRecovery { get; set; }
}
