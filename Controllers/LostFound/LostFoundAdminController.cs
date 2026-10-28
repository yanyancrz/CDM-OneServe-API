using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.LostFound;
using CDM_OneServe_API.Services.LostFound;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.LostFound;

/// <summary>
/// The Lost &amp; Found admin console - read-only listings plus
/// the review actions.
///
/// Access:
///   - LostFoundSuperAdmin / LostFoundAdmin / Admin / SuperAdmin: everything.
///   - LostFoundSubAdmin: reports, claims, pickups and their own profile.
///   - Administrator management, login activity and the activity log are
///     Super-Admin-only and are re-checked in the service.
/// </summary>
[ApiController]
[Route("api/lostfound/admin")]
[Authorize(Roles = LostFoundRoles.Access)]
public class LostFoundAdminController : LostFoundControllerBase
{
    private readonly LostFoundService _service;
    private readonly LostFoundAdminService _admins;

    public LostFoundAdminController(
        LostFoundService service,
        LostFoundAdminService admins)
    {
        _service = service;
        _admins = admins;
    }

    // =========================================================
    // SESSION / IDENTITY
    // =========================================================

    /// <summary>
    /// The signed-in console user and what they may do. This is the one
    /// endpoint that still answers while a forced password change is
    /// pending, so the console can render the change-password screen.
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _admins.GetMeAsync(userId, ct));
    }

    // =========================================================
    // READ-ONLY CONSOLE DATA
    // =========================================================

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

    // =========================================================
    // ADMIN MANAGEMENT (SUPER ADMIN)
    // =========================================================

    [HttpGet("admins")]
    [Authorize(Roles = LostFoundRoles.Management)]
    public async Task<IActionResult> GetAdmins(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _admins.GetAdminsAsync(userId, ct));
    }

    [HttpPost("admins")]
    [Authorize(Roles = LostFoundRoles.Management)]
    public async Task<IActionResult> CreateAdmin(
        [FromBody] CreateSubAdminDto dto,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _admins.CreateSubAdminAsync(userId, dto, ct));
    }

    [HttpPut("admins/{id:int}/status")]
    [Authorize(Roles = LostFoundRoles.Management)]
    public async Task<IActionResult> SetAdminStatus(
        int id,
        [FromBody] UpdateAdminStatusDto dto,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _admins.SetAdminStatusAsync(userId, id, dto.IsActive, ct));
    }

    // =========================================================
    // LOGIN MONITORING + AUDIT (SUPER ADMIN)
    // =========================================================

    [HttpGet("login-activity")]
    [Authorize(Roles = LostFoundRoles.Management)]
    public async Task<IActionResult> GetLoginActivity(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        var sessionKey = Request.Headers["X-Session-Key"].ToString();
        return ToAction(await _admins.GetLoginActivityAsync(userId, sessionKey, ct));
    }

    [HttpGet("activity-log")]
    [Authorize(Roles = LostFoundRoles.Management)]
    public async Task<IActionResult> GetActivityLog(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? entityType = null,
        CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _admins.GetActivityLogAsync(userId, page, pageSize, entityType, ct));
    }
}
