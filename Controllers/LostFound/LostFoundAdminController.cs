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

    [HttpGet("availability")]
    public async Task<IActionResult> GetAvailability()
    {
        return Ok(ApiResponse.Ok(await _service.GetAvailabilityAsync()));
    }
}
