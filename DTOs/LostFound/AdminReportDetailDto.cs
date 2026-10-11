namespace CDM_OneServe_API.DTOs.LostFound;

/// <summary>
/// Everything the admin console shows on one item's detail view:
/// the report, who filed it, every claim on it, the latest recovery
/// (lost reports) and every match it appears in.
/// </summary>
public class AdminReportDetailDto
{
    public ReportDto Report { get; set; } = new();

    public string ReporterName { get; set; } = string.Empty;

    public string ReporterEmail { get; set; } = string.Empty;

    public string ReporterIdNumber { get; set; } = string.Empty;

    public List<ClaimAdminDto> Claims { get; set; } = new();

    public RecoveryDto? Recovery { get; set; }

    public List<MatchDto> Matches { get; set; } = new();
}
