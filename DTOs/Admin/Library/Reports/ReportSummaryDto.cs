namespace CDM_OneServe_API.DTOs.Admin.Library.Reports;

public class ReportSummaryDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }

    public int TotalCirculation { get; set; }
    public int TotalReturns { get; set; }

    public decimal FinesCollected { get; set; }
    public decimal OutstandingFines { get; set; }
    public decimal AccruingFines { get; set; }

    public int ActiveLoans { get; set; }
    public int OverdueLoans { get; set; }

    // Percent (0 to 100). Null when nothing was returned in the range.
    public double? OnTimeReturnRate { get; set; }
}