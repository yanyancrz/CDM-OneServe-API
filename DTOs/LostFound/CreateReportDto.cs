namespace CDM_OneServe_API.DTOs.LostFound;

public class CreateReportDto
{
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


    public string? ImagePath { get; set; }


    // =========================================================
    // ITEM CUSTODY
    //
    // Student     = Finder currently has item.
    // AdminOffice = Item was given to Admin Office.
    // None        = Lost report / not applicable.
    // =========================================================

    public string ItemCustody { get; set; } =
        "None";
}