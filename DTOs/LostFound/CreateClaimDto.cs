namespace CDM_OneServe_API.DTOs.LostFound;

public class CreateClaimDto
{
    public int ReportId { get; set; }

    public string VerificationNotes { get; set; } = string.Empty;
}