using System.Security.Claims;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Guidance;
using CDM_OneServe_API.DTOs.Guidance.Admin;
using CDM_OneServe_API.Services.Guidance;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Guidance;

/// <summary>
/// Guidance Head portal (desktop). Everything the Guidance Administration
/// frontend calls lives here.
///
///   GET    /api/admin/guidance/me
///   POST   /api/admin/guidance/change-password
///   GET    /api/admin/guidance/dashboard/overview
///   GET    /api/admin/guidance/counselors
///   GET    /api/admin/guidance/counselors/{id}
///   POST   /api/admin/guidance/counselors
///   PUT    /api/admin/guidance/counselors/{id}
///   PATCH  /api/admin/guidance/counselors/{id}/status
///   POST   /api/admin/guidance/counselors/{id}/reset-password
///   GET    /api/admin/guidance/students
///   GET    /api/admin/guidance/students/{id}
///   GET    /api/admin/guidance/reports/overview
///   GET    /api/admin/guidance/settings
///   PUT    /api/admin/guidance/settings
///
/// There is no separate login: the same /api/auth/login issues the JWT and
/// the role in the token decides where the user lands.
/// </summary>
[ApiController]
[Route("api/admin/guidance")]
[Authorize(Roles = GuidanceRoles.HeadAccess)]
public class AdminGuidanceHeadController : ControllerBase
{
    private readonly AdminGuidanceHeadService _service;

    public AdminGuidanceHeadController(AdminGuidanceHeadService service)
    {
        _service = service;
    }

    // --------------------------------------------------
    // ACCESS
    // --------------------------------------------------
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        var role = User.FindFirstValue(ClaimTypes.Role);

        return ToAction(await _service.GetMeAsync(userId, role, ct));
    }

    // --------------------------------------------------
    // ACCOUNT
    // --------------------------------------------------
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] GuidanceChangePasswordRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _service.ChangeOwnPasswordAsync(userId, request, ct));
    }

    // --------------------------------------------------
    // DASHBOARD
    // --------------------------------------------------
    [HttpGet("dashboard/overview")]
    public async Task<IActionResult> GetOverview(CancellationToken ct) =>
        Ok(ApiResponse.Ok(await _service.GetOverviewAsync(ct)));

    // --------------------------------------------------
    // COUNSELORS
    // --------------------------------------------------
    [HttpGet("counselors")]
    public async Task<IActionResult> GetCounselors([FromQuery] GuidanceCounselorFilterDto filter, CancellationToken ct) =>
        Ok(ApiResponse.Ok(await _service.GetCounselorsAsync(filter, ct)));

    [HttpGet("counselors/{id:int}")]
    public async Task<IActionResult> GetCounselor(int id, CancellationToken ct) =>
        ToAction(await _service.GetCounselorAsync(id, ct));

    [HttpPost("counselors")]
    public async Task<IActionResult> CreateCounselor([FromBody] CreateCounselorRequest request, CancellationToken ct) =>
        ToAction(await _service.CreateCounselorAsync(request, ct));

    [HttpPut("counselors/{id:int}")]
    public async Task<IActionResult> UpdateCounselor(int id, [FromBody] UpdateCounselorRequest request, CancellationToken ct) =>
        ToAction(await _service.UpdateCounselorAsync(id, request, ct));

    [HttpPatch("counselors/{id:int}/status")]
    public async Task<IActionResult> SetCounselorStatus(int id, [FromBody] UpdateAccountStatusRequest request, CancellationToken ct) =>
        ToAction(await _service.SetCounselorStatusAsync(id, request.Status, ct));

    [HttpPost("counselors/{id:int}/reset-password")]
    public async Task<IActionResult> SendPasswordReset(int id, CancellationToken ct) =>
        ToAction(await _service.SendPasswordResetAsync(id, ct));

    // --------------------------------------------------
    // STUDENTS (read-only directory)
    // --------------------------------------------------
    [HttpGet("students")]
    public async Task<IActionResult> GetStudents([FromQuery] GuidanceStudentFilterDto filter, CancellationToken ct) =>
        Ok(ApiResponse.Ok(await _service.GetStudentsAsync(filter, ct)));

    [HttpGet("students/{id:int}")]
    public async Task<IActionResult> GetStudent(int id, CancellationToken ct) =>
        ToAction(await _service.GetStudentAsync(id, ct));

    // --------------------------------------------------
    // REPORTS
    // --------------------------------------------------
    [HttpGet("reports/overview")]
    public async Task<IActionResult> GetReports([FromQuery] int days = 30, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await _service.GetReportsAsync(days, ct)));

    // --------------------------------------------------
    // SETTINGS
    // --------------------------------------------------
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct) =>
        Ok(ApiResponse.Ok(await _service.GetSettingsAsync(ct)));

    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] GuidanceModuleSettings settings, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return Ok(ApiResponse.Ok(await _service.SaveSettingsAsync(settings, userId, ct), "Settings saved."));
    }

    // --------------------------------------------------
    // HELPERS
    // --------------------------------------------------
    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private IActionResult InvalidSession() =>
        Unauthorized(ApiResponse.Fail("Invalid session. Please log in again."));

    private IActionResult ToAction<T>(GuidanceResult<T> result) =>
        result.Success
            ? StatusCode(result.Status, ApiResponse.Ok(result.Data!, result.Message))
            : StatusCode(result.Status, ApiResponse.Fail(result.Message ?? "Request failed."));
}
