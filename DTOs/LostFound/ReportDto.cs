namespace CDM_OneServe_API.DTOs.LostFound;

public class ReportDto
{
    public int ReportId { get; set; }
    public int UserId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime DateLostFound { get; set; }
    public string ReportType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ImagePath { get; set; }
    public string ItemCustody { get; set; } = "None";
    public DateTime CreatedAt { get; set; }

    // ---- handed-over (Returned) info ----
    // Found reports stay on the board even after the handover, so the
    // board shows who the item went back to: the receiver name the
    // admin typed at pickup, and the claimant account that filed the
    // claim. No student ID is exposed here.

    /** A Returned claim exists on this report. */
    public bool IsReturned { get; set; }

    /** Receiver name entered by the admin at pickup. */
    public string? ReturnedToName { get; set; }

    /** Full name of the claimant account that filed the claim. */
    public string? ClaimedByName { get; set; }

    /** When the handover was recorded. */
    public DateTime? ReturnedAt { get; set; }

    // ---- review attribution (admin console) ----

    /** Administrator who published/rejected this report. */
    public int? ReviewedByUserId { get; set; }

    public string? ReviewedByName { get; set; }

    public string? ReviewedByRole { get; set; }

    public DateTime? ReviewedAt { get; set; }
}
