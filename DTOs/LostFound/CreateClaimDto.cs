namespace CDM_OneServe_API.DTOs.LostFound;

/// <summary>
/// Submit (or update) a claim. Every field except ReportId is optional
/// EXCEPT the ones the service requires when no proof file is attached:
/// IdentifyingDetails and NoProofReason.
///
/// OwnershipExplanation is legacy: the "why is this yours?" question was
/// removed from the claim form, so new claims never send it. An older
/// client that still does gets the value stored, but nothing requires it.
/// </summary>
public class CreateClaimDto
{
    public int ReportId { get; set; }

    /// <summary>Legacy single-paragraph field; mirrors OwnershipExplanation.</summary>
    public string VerificationNotes { get; set; } = string.Empty;

    public string? OwnershipExplanation { get; set; }

    /// <summary>Unique marks, contents, serial numbers, stickers...</summary>
    public string? IdentifyingDetails { get; set; }

    public string? NoProofReason { get; set; }

    /// <summary>
    /// Path returned by POST /api/lostfound/claims/proof-upload. The
    /// endpoint itself has already validated the file; the path is
    /// re-checked here so an old or foreign file can never be claimed.
    /// </summary>
    public string? ProofAttachmentPath { get; set; }
}
