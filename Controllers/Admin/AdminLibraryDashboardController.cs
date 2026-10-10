using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.Interfaces.Admin.Library;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
[Route("api/admin/library/dashboard")]
[Authorize(Roles = LibraryRoles.Access)]
public class AdminLibraryDashboardController : ControllerBase
{
    private readonly IAdminDashboardService _dashboardService;

    public AdminLibraryDashboardController(IAdminDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    // GET /api/admin/library/dashboard
    [HttpGet]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken)
    {
        var stats = await _dashboardService.GetStatsAsync(cancellationToken);
        return Ok(ApiResponse.Ok(stats));
    }

    // GET /api/admin/library/dashboard/overdue?limit=10
    [HttpGet("overdue")]
    public async Task<IActionResult> GetOverdue([FromQuery] int limit = 10, CancellationToken cancellationToken = default)
    {
        var items = await _dashboardService.GetOverdueAsync(limit, cancellationToken);
        return Ok(ApiResponse.Ok(items));
    }

    // GET /api/admin/library/dashboard/activity?limit=10
    // GET /api/admin/library/dashboard/activity?limit=10&scope=mine|all
    [HttpGet("activity")]
    public async Task<IActionResult> GetActivity(
        [FromQuery] int limit = 10,
        [FromQuery] string scope = "mine",
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized(ApiResponse.Fail("Invalid session. Please log in again."));

        // Only the Library Head may see everyone. Staff are forced to "mine".
        var allStaff = scope == "all" && User.IsInRole("LibraryAdmin");

        var items = await _dashboardService.GetRecentActivityAsync(limit, userId, allStaff, cancellationToken);
        return Ok(ApiResponse.Ok(items));
    }
}