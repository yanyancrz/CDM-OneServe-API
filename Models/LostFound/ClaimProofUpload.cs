using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.Models.LostFound;

/// <summary>
/// A proof-of-ownership file staged by a claimant BEFORE it is attached
/// to a claim. Staging keeps the upload independent of the claim: a file
/// that is never attached simply never gets linked to anything and stays
/// unreadable through the API.
///
/// The file itself lives outside the public web root (see
/// ClaimProofStorage), so it can only be served through the authorized
/// endpoint - never from a public item listing.
/// </summary>
public class ClaimProofUpload
{
    [Key]
    public int Id { get; set; }

    /// <summary>Who uploaded it - only this user may attach or view it.</summary>
    public int UploadedByUserId { get; set; }

    /// <summary>Null until a claim actually uses this file.</summary>
    public int? ClaimId { get; set; }

    /// <summary>Random server-side name; the original is kept only for display.</summary>
    public string StoredPath { get; set; } = string.Empty;

    public string OriginalFileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
