using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.Models.LostFound;

public class LostFoundReport
{
    [Key]
    public int ReportId { get; set; }


    public int UserId { get; set; }


    public string ItemName { get; set; } =
        string.Empty;


    public string Description { get; set; } =
        string.Empty;


    public string Category { get; set; } =
        string.Empty;


    public string Location { get; set; } =
        string.Empty;


    public DateTime DateLostFound { get; set; }


    public string ReportType { get; set; } =
        string.Empty;


    public string Status { get; set; } =
        "Pending";


    public string? ImagePath { get; set; }


    public DateTime CreatedAt { get; set; } =
        DateTime.Now;


    // =========================================================
    // ITEM CUSTODY
    //
    // Student:
    // The finder currently has the physical item.
    //
    // AdminOffice:
    // The finder already gave the physical item
    // to the FIND Admin Office.
    //
    // None:
    // Normally used for Lost reports.
    // =========================================================

    public string ItemCustody { get; set; } =
        "None";

    // =========================================================
    // REVIEW ATTRIBUTION
    //
    // Snapshots of the signed-in administrator who published or
    // rejected this report. The name and role are copied here so
    // the audit trail stays readable even if the account is later
    // renamed, deactivated or deleted.
    // =========================================================

    public int? ReviewedByUserId { get; set; }

    public string? ReviewedByName { get; set; }

    public string? ReviewedByRole { get; set; }

    public DateTime? ReviewedAt { get; set; }
}