namespace CDM_OneServe_API.DTOs.LostFound;

public class UpdateStatusDto
{
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Reason for a rejection, or the message telling the claimant what
    /// extra information the reviewer needs. Required for both.
    /// </summary>
    public string? Reason { get; set; }
}
