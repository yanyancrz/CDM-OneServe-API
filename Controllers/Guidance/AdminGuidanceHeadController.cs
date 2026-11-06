using System.Security.Claims;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Guidance;
using CDM_OneServe_API.DTOs.Guidance.Admin;
using CDM_OneServe_API.Services.Guidance;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
    private readonly GuidanceNarrativeService _narrative;
    private readonly IWebHostEnvironment _env;

    public AdminGuidanceHeadController(
        AdminGuidanceHeadService service,
        GuidanceNarrativeService narrative,
        IWebHostEnvironment env)
    {
        _service = service;
        _narrative = narrative;
        _env = env;
    }

    private string ActorRole => User.FindFirstValue(ClaimTypes.Role) ?? "GuidanceAdmin";

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

        return ToAction(await _service.ChangeOwnPasswordAsync(
            userId,
            request,
            User.GetTokenIssuedAt(),
            ct));
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

    [HttpPost("counselors/{id:int}/photo")]
    public async Task<IActionResult> UploadCounselorPhoto(int id, IFormFile file, CancellationToken ct) =>
        ToAction(await _service.UploadCounselorPhotoAsync(id, file, ct));

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
    // NARRATIVE REPORTS (canonical, DB-backed, all counselors)
    // --------------------------------------------------
    [HttpGet("narrative-reports")]
    public async Task<IActionResult> GetNarrativeReports(
        [FromQuery] NarrativeReportFilterDto filter, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(ApiResponse.Ok(await _narrative.GetAdminListAsync(filter, ct)));
    }

    [HttpGet("narrative-reports/stats")]
    public async Task<IActionResult> GetNarrativeReportStats(CancellationToken ct) =>
        Ok(ApiResponse.Ok(await _narrative.GetAdminStatsAsync(ct)));

    [HttpGet("narrative-reports/subjects")]
    public async Task<IActionResult> GetNarrativeSubjects(
        [FromQuery] string? search, [FromQuery] int take = 50, CancellationToken ct = default) =>
        Ok(ApiResponse.Ok(await _narrative.GetSubjectsAsync(search, take, ct)));

    [HttpGet("narrative-reports/{id:int}")]
    public async Task<IActionResult> GetNarrativeReport(int id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _narrative.GetAdminDetailAsync(userId, ActorRole, id, recordView: true, ct));
    }

    [HttpGet("narrative-reports/{id:int}/events")]
    public async Task<IActionResult> GetNarrativeReportEvents(int id, CancellationToken ct) =>
        Ok(ApiResponse.Ok(await _narrative.GetEventsAsync(id, ct)));

    [HttpGet("students/{id:int}/narrative-reports")]
    public async Task<IActionResult> GetStudentNarrativeReports(
        int id, [FromQuery] NarrativeReportFilterDto filter, CancellationToken ct)
    {
        filter.StudentId = id;
        return Ok(ApiResponse.Ok(await _narrative.GetAdminListAsync(filter, ct)));
    }

    [HttpGet("counselors/{id:int}/narrative-reports")]
    public async Task<IActionResult> GetCounselorNarrativeReports(
        int id, [FromQuery] NarrativeReportFilterDto filter, CancellationToken ct)
    {
        filter.CounselorId = id;
        return Ok(ApiResponse.Ok(await _narrative.GetAdminListAsync(filter, ct)));
    }

    [HttpGet("narrative-reports/{id:int}/attachments/{attachmentId:int}/download")]
    public async Task<IActionResult> DownloadNarrativeAttachment(
        int id, int attachmentId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store, private";
        if (!TryGetUserId(out var userId)) return InvalidSession();

        var r = await _narrative.DownloadAttachmentAsync(
            userId, ActorRole, isAdmin: true, id, attachmentId, ct);

        return r.Success && r.Data != null
            ? PhysicalFile(r.Data.FilePath, r.Data.ContentType, r.Data.FileName)
            : StatusCode(r.Status, ApiResponse.Fail(r.Message ?? "Request failed."));
    }

    [HttpGet("narrative-reports/{id:int}/export")]
    public async Task<IActionResult> ExportNarrativeReport(
        int id, [FromQuery] string format = "docx", CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store, private";
        if (!TryGetUserId(out var userId)) return InvalidSession();

        var r = await _narrative.ExportAdminAsync(userId, ActorRole, id, format, ct);

        return r.Success && r.Data != null
            ? File(r.Data.Bytes, r.Data.ContentType, r.Data.FileName)
            : StatusCode(r.Status, ApiResponse.Fail(r.Message ?? "Request failed."));
    }

    // --------------------------------------------------
    // LEGACY NARRATIVE FILE REPOSITORY
    // Kept for existing uploaded files; the canonical database-backed reports
    // live under /narrative-reports above.
    // --------------------------------------------------
    [HttpGet("reports/narrative/files")]
    public IActionResult GetNarrativeReports()
    {
        var dir = GetNarrativeDir();
        Directory.CreateDirectory(dir);
        var files = Directory.GetFiles(dir)
            .Select(f => new FileInfo(f))
            .Select(fi => new
            {
                id = fi.Name,
                name = fi.Name,
                size = fi.Length,
                uploadedAt = fi.CreationTimeUtc,
                url = "/uploads/guidance-reports/" + fi.Name,
            })
            .OrderByDescending(f => f.uploadedAt)
            .ToList();
        return Ok(ApiResponse.Ok(files));
    }

    [HttpPost("reports/narrative/files")]
    public async Task<IActionResult> UploadNarrativeReport(IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0) return BadRequest(ApiResponse.Fail("No file uploaded."));

        var dir = GetNarrativeDir();
        Directory.CreateDirectory(dir);

        var safeName = Path.GetFileName(file.FileName);
        var storedName = Guid.NewGuid().ToString("N") + "_" + safeName;
        var path = Path.Combine(dir, storedName);

        await using (var stream = System.IO.File.Create(path))
        {
            await file.CopyToAsync(stream, ct);
        }

        var fi = new FileInfo(path);
        return Ok(ApiResponse.Ok(new
        {
            id = fi.Name,
            name = safeName,
            size = fi.Length,
            uploadedAt = fi.CreationTimeUtc,
            url = "/uploads/guidance-reports/" + fi.Name,
        }, "Uploaded."));
    }

    [HttpDelete("reports/narrative/files/{id}")]
    public IActionResult DeleteNarrativeReport(string id)
    {
        var dir = GetNarrativeDir();
        var path = Path.Combine(dir, Path.GetFileName(id));
        if (!System.IO.File.Exists(path)) return NotFound(ApiResponse.Fail("File not found."));
        System.IO.File.Delete(path);
        return Ok(ApiResponse.Ok<object?>(null, "Deleted."));
    }

    [HttpPost("reports/narrative/files/{id}/rename")]
    public IActionResult RenameNarrativeReport(string id, [FromQuery] string newName)
    {
        var dir = GetNarrativeDir();
        var oldPath = Path.Combine(dir, Path.GetFileName(id));
        if (!System.IO.File.Exists(oldPath)) return NotFound(ApiResponse.Fail("File not found."));

        var safeName = Path.GetFileName(newName ?? "");
        if (string.IsNullOrWhiteSpace(safeName)) return BadRequest(ApiResponse.Fail("Invalid name."));

        var newPath = Path.Combine(dir, safeName);
        if (oldPath == newPath) return Ok(ApiResponse.Ok<object?>(null, "Unchanged."));
        if (System.IO.File.Exists(newPath)) return Conflict(ApiResponse.Fail("A file with that name already exists."));

        System.IO.File.Move(oldPath, newPath);
        var fi = new FileInfo(newPath);
        return Ok(ApiResponse.Ok(new
        {
            id = fi.Name,
            name = safeName,
            size = fi.Length,
            uploadedAt = fi.CreationTimeUtc,
            url = "/uploads/guidance-reports/" + fi.Name,
        }, "Renamed."));
    }

    private string GetNarrativeDir() =>
        Path.Combine(_env.WebRootPath ?? _env.ContentRootPath, "uploads", "guidance-reports");

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
