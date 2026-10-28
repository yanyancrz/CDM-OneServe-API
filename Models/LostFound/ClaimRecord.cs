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

    // Kept for backwards compatibility: it still carries the claimant's
    // written explanation (the "why this is mine" paragraph).
    public string VerificationNotes { get; set; } = string.Empty;

    public string Status { get; set; } = "Processing";

    // =========================================================
    // PROOF OF OWNERSHIP (all optional - a claimant may have
    // no receipt, certificate or photo at all)
    // =========================================================

    /// <summary>Why the claimant believes the item is theirs.</summary>
    public string? OwnershipExplanation { get; set; }

    /// <summary>Marks, contents, serials and other non-public details.</summary>
    public string? IdentifyingDetails { get; set; }

    /// <summary>Required only when no proof file is attached.</summary>
    public string? NoProofReason { get; set; }

    /// <summary>Server-side path under uploads/lostfound/claims. Never public.</summary>
    public string? ProofAttachmentPath { get; set; }

    /// <summary>Original file name, for display only.</summary>
    public string? ProofFileName { get; set; }

    /// <summary>Sniffed content type (image/* or application/pdf).</summary>
    public string? ProofContentType { get; set; }

    public long ProofFileSize { get; set; }

    // =========================================================
    // ADMIN DECISION (rejection reason or information request)
    // =========================================================

    public string? DecisionReason { get; set; }

    public DateTime? DecisionAt { get; set; }

    public int? DecisionByUserId { get; set; }

    // Snapshots of the administrator who made the decision, so the
    // console can show "Approved by: Maria Santos (Sub-Admin)" without
    // a join and without losing the name if the account changes later.
    public string? DecisionByName { get; set; }

    public string? DecisionByRole { get; set; }
}
