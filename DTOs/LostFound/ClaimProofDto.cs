namespace CDM_OneServe_API.DTOs.LostFound;

/// <summary>
/// A staged proof file, returned by the upload endpoint. The client keeps
/// only the stored path, which is re-validated when the claim is saved.
/// </summary>
public class ClaimProofDto
{
    public int UploadId { get; set; }

    /// <summary>Server-side reference the claim stores. Not a public URL.</summary>
    public string StoredPath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }
}
