using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.LostFound;
using CDM_OneServe_API.Services.LostFound;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.LostFound;

/// <summary>
/// The Lost &amp; Found admin console - read-only listings plus
/// the review actions. Everything here is gated by the
/// LostFoundAdmin role (OneServe's built-in Admin and SuperAdmin
/// pass too, since they administer every module).
/// </summary>
[ApiController]
[Route("api/lostfound/admin")]
[Authorize(Roles = "LostFoundAdmin,Admin,SuperAdmin")]
public class LostFoundAdminController : LostFoundControllerBase
{
    private readonly LostFoundService _service;

    public LostFoundAdminController(LostFoundService service) => _service = service;

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        return Ok(ApiResponse.Ok(await _service.GetAdminStatsAsync()));
    }

    [HttpGet("reports")]
    public async Task<IActionResult> GetReports()
    {
        return Ok(ApiResponse.Ok(await _service.GetAllReportsAsync()));
    }

    [HttpGet("reports/{id:int}")]
    public async Task<IActionResult> GetReport(int id)
    {
        var r = await _service.GetAdminReportAsync(id);
        return r.Success ? Ok(ApiResponse.Ok(r.Data)) : StatusCode(r.Status, ApiResponse.Fail(r.Message ?? "Request failed."));
    }

    [HttpGet("claims")]
    public async Task<IActionResult> GetClaims()
    {
        return Ok(ApiResponse.Ok(await _service.GetAllClaimsAsync()));
    }

    [HttpGet("schedules")]
    public async Task<IActionResult> GetSchedules()
    {
        return Ok(ApiResponse.Ok(await _service.GetAllSchedulesAsync()));
    }

    [HttpGet("matches")]
    public async Task<IActionResult> GetMatches()
    {
        return Ok(ApiResponse.Ok(await _service.GetAllMatchesAsync()));
    }

    /// <summary>
    /// Recomputes automatic matches for every published report. Used once
    /// after the feature ships (older approvals predate it) and whenever
    /// the board needs a resync. Confirmed pairs are never rescored.
    /// </summary>
    [HttpPost("matches/recompute")]
    public async Task<IActionResult> RecomputeMatches()
    {
        var count = await _service.RecomputeAllAutoMatchesAsync();

        // Anonymous object, not a bare int: ApiResponse<T>.Data cannot be a
        // value type (WhenWritingNull is invalid on one - it 500s).
        return Ok(ApiResponse.Ok(new { rescanned = count }));
    }

    [HttpGet("availability")]
    public async Task<IActionResult> GetAvailability()
    {
        return Ok(ApiResponse.Ok(await _service.GetAvailabilityAsync()));
    }
}
