using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Reports;
using CDM_OneServe_API.Interfaces.Admin.Library;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin;

// READ ONLY: this controller has no POST, PUT, or DELETE.
[ApiController]
[Route("api/admin/library/reports")]
[Authorize(Roles = LibraryRoles.Access)]
public class AdminLibraryReportsController : ControllerBase
{
    private readonly IAdminReportService _reports;

    public AdminLibraryReportsController(IAdminReportService reports)
    {
        _reports = reports;
    }

    // GET /api/admin/library/reports/summary?from=&to=
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] ReportFilterDto filter, CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await _reports.GetSummaryAsync(filter, cancellationToken)));

    // GET /api/admin/library/reports/circulation?from=&to=&granularity=daily|weekly|monthly
    [HttpGet("circulation")]
    public async Task<IActionResult> GetCirculation([FromQuery] ReportFilterDto filter, CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await _reports.GetCirculationAsync(filter, cancellationToken)));

    // GET /api/admin/library/reports/institutes?from=&to=
    [HttpGet("institutes")]
    public async Task<IActionResult> GetInstitutes([FromQuery] ReportFilterDto filter, CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await _reports.GetInstitutesAsync(filter, cancellationToken)));

    // GET /api/admin/library/reports/top-books?from=&to=&limit=10
    [HttpGet("top-books")]
    public async Task<IActionResult> GetTopBooks(
        [FromQuery] ReportFilterDto filter, [FromQuery] int limit = 10, CancellationToken cancellationToken = default)
        => Ok(ApiResponse.Ok(await _reports.GetTopBooksAsync(filter, limit, cancellationToken)));

    // GET /api/admin/library/reports/overdue
    [HttpGet("overdue")]
    public async Task<IActionResult> GetOverdue([FromQuery] ReportFilterDto filter, CancellationToken cancellationToken)
        => ToAction(await _reports.GetTableAsync("overdue", filter, cancellationToken));

    // GET /api/admin/library/reports/export/{dataset}?from=&to=
    // dataset: catalog | active-loans | overdue | borrows | returns | reservations
    [HttpGet("export/{dataset}")]
    public async Task<IActionResult> Export(string dataset, [FromQuery] ReportFilterDto filter, CancellationToken cancellationToken)
        => ToAction(await _reports.GetTableAsync(dataset, filter, cancellationToken));

    private IActionResult ToAction<T>(LibraryResult<T> result) =>
        result.Error == null
            ? StatusCode(result.StatusCode, ApiResponse.Ok(result.Data))
            : StatusCode(result.StatusCode, ApiResponse.Fail(result.Error));
}