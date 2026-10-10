namespace CDM_OneServe_API.DTOs.Admin.Library.Reports;

public class ReportFilterDto
{
    // Inclusive dates. Default: the last 30 days.
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    // daily (default) | weekly | monthly
    public string? Granularity { get; set; }
}