using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Reports;

namespace CDM_OneServe_API.Interfaces.Admin.Library;

public interface IAdminReportService
{
    Task<ReportSummaryDto> GetSummaryAsync(ReportFilterDto filter, CancellationToken cancellationToken);

    Task<CirculationReportDto> GetCirculationAsync(ReportFilterDto filter, CancellationToken cancellationToken);

    Task<List<InstituteReportDto>> GetInstitutesAsync(ReportFilterDto filter, CancellationToken cancellationToken);

    Task<List<TopBookDto>> GetTopBooksAsync(ReportFilterDto filter, int limit, CancellationToken cancellationToken);

    // dataset: catalog | active-loans | overdue | borrows | returns | reservations
    Task<LibraryResult<ExportTableDto>> GetTableAsync(string dataset, ReportFilterDto filter, CancellationToken cancellationToken);
}