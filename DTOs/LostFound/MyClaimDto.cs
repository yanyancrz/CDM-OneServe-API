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
}
