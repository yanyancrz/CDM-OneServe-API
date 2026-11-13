using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models.Platform;
using CDM_OneServe_API.Services.Admin.Platform;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers.Admin.Platform;

/// <summary>
/// Central, read-only oversight of the four services, plus the Clinic
/// integration controls.
///
/// These endpoints summarise the EXISTING module tables. They never duplicate
/// a module console and never expose confidential content:
///   - Library: counts, loans, reservations, patron totals
///   - Guidance: counts only - no narrative text, no private notes, no chat
///   - Lost &amp; Found: counts and item rows - no proof files
///   - Clinic: integration health and usage markers - no medical records
///
/// Opening a service page is recorded once per admin per 30 minutes, so the
/// audit trail shows who reviewed each service without one row per refresh.
/// </summary>
[Route("api/admin/services")]
public class AdminServicesController : AdminPlatformControllerBase
{
    private const string Module = "System";

    private readonly AdminServiceSummaryService _summaries;
    private readonly ClinicIntegrationLogger _clinic;
    private readonly SystemHealthService _health;
    private readonly SystemSettingsService _settings;
    private readonly AppDbContext _db;

    public AdminServicesController(
        AdminServiceSummaryService summaries,
        ClinicIntegrationLogger clinic,
        SystemHealthService health,
        SystemSettingsService settings,
        AppDbContext db,
        PermissionService permissions,
        AdminAuditService audit,
        SecurityEventService security)
        : base(permissions, audit, security)
    {
        _summaries = summaries;
        _clinic = clinic;
        _health = health;
        _settings = settings;
        _db = db;
    }

    // ---------------------------------------------------------
    // LIBRARY
    // ---------------------------------------------------------

    // GET: api/admin/services/library
    [HttpGet("library")]
    public async Task<IActionResult> GetLibrary(CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.LibraryView, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var data = await _summaries.GetLibraryAsync(ct);

        await RecordServiceViewAsync(
            auth.Identity!, "Library", "Library", ct);

        return Ok(new
        {
            module = "Library",
            enabled = await _settings.GetBoolAsync("modules.library.enabled", ct),
            canManage = auth.Identity!.Can(
                PermissionCatalog.LibraryManage, PermissionLevels.Manage),
            // The module owner keeps write control; the central portal is
            // read-only unless the matrix explicitly grants Library manage.
            note = "Read-only oversight. Book, loan and settings changes stay with the Library Head unless 'Library - manage' is granted.",
            library = data
        });
    }

    // ---------------------------------------------------------
    // GUIDANCE
    // ---------------------------------------------------------

    // GET: api/admin/services/guidance
    [HttpGet("guidance")]
    public async Task<IActionResult> GetGuidance(CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.GuidanceView, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var data = await _summaries.GetGuidanceAsync(ct);

        await RecordServiceViewAsync(
            auth.Identity!, "Guidance", "Guidance", ct);

        return Ok(new
        {
            module = "Guidance",
            enabled = await _settings.GetBoolAsync("modules.guidance.enabled", ct),
            canManage = auth.Identity!.Can(
                PermissionCatalog.GuidanceManage, PermissionLevels.Manage),
            // Part 7.6 / 7.7: counts and availability only.
            narrativeContentsExposed = false,
            chatContentsExposed = false,
            note = "Counts only. Counseling narratives, private notes and chat messages are never returned to the central portal.",
            guidance = data
        });
    }

    // ---------------------------------------------------------
    // LOST & FOUND
    // ---------------------------------------------------------

    // GET: api/admin/services/lost-found
    [HttpGet("lost-found")]
    public async Task<IActionResult> GetLostFound(CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.LostFoundView, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var data = await _summaries.GetLostFoundAsync(ct);

        await RecordServiceViewAsync(
            auth.Identity!, "LostFound", "Lost & Found", ct);

        return Ok(new
        {
            module = "Lost & Found",
            enabled = await _settings.GetBoolAsync("modules.lostfound.enabled", ct),
            canManage = auth.Identity!.Can(
                PermissionCatalog.LostFoundManage, PermissionLevels.Manage),
            proofFilesExposed = false,
            note = "Reports and counts are shown. Claim proof files stay inside the Lost & Found console.",
            lostFound = data
        });
    }

    // ---------------------------------------------------------
    // CLINIC
    // ---------------------------------------------------------

    // GET: api/admin/services/clinic
    [HttpGet("clinic")]
    public async Task<IActionResult> GetClinic(CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.ClinicView, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var connection = await _health.CheckClinicAsync(ct);
        var summary = await _clinic.GetSummaryAsync(ct);

        await RecordServiceViewAsync(
            auth.Identity!, "Clinic", "Clinic", ct);

        return Ok(new
        {
            module = "Clinic",
            enabled = await _settings.GetBoolAsync("clinic.access.enabled", ct),
            canManage = auth.Identity!.Can(
                PermissionCatalog.ClinicManage, PermissionLevels.Manage),
            connection,
            lastCheckedAt = connection.CheckedAt,
            // Explicit, so no client can assume this page carries health data.
            medicalRecordsExposed = false,
            canViewMedicalRecords = await ClinicMedicalRecordsAccess.CanViewAsync(_db, auth.Identity!, ct),
            note = "Medical records are available to the super administrator in the Medical records tab. Records are retrieved from the Clinic CMS.",
            summary
        });
    }

    // GET: api/admin/services/clinic/logs
    [HttpGet("clinic/logs")]
    public async Task<IActionResult> GetClinicLogs(
        [FromQuery] bool? success,
        [FromQuery] bool failuresOnly = false,
        [FromQuery] string? resource = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.ClinicView, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var items = await _clinic.QueryLogsAsync(
            success, resource, failuresOnly, from, page, pageSize, ct);

        return Ok(new
        {
            items,
            page,
            pageSize = Math.Clamp(pageSize, 10, 100),
            hasMore = items.Count == Math.Clamp(pageSize, 10, 100),
            note = "Integration metadata only. No medical payload, integration key or student number is stored."
        });
    }

    // POST: api/admin/services/clinic/test-connection
    [HttpPost("clinic/test-connection")]
    public async Task<IActionResult> TestClinicConnection(CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.ClinicManage, PermissionLevels.Manage, ct);

        if (auth.Failure is not null) return auth.Failure;

        var result = await _health.CheckClinicAsync(ct);

        await _db.SystemHealthChecks.AddAsync(
            new SystemHealthSnapshot
            {
                Component = result.Component,
                Status = result.Status,
                LatencyMs = result.LatencyMs,
                Message = result.Message,
                CheckedAt = result.CheckedAt
            }, ct);

        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync(
            "Clinic", "Clinic",
            "Tested the Clinic CMS connection",
            entityType: "Integration",
            previousStatus: null,
            newStatus: result.Status,
            details: result.Message,
            ct: ct);

        return Ok(new { result, testedAt = result.CheckedAt });
    }

    // PUT: api/admin/services/clinic/access
    [HttpPut("clinic/access")]
    public async Task<IActionResult> SetClinicAccess(
        [FromBody] ServiceToggleRequest request,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.ClinicManage, PermissionLevels.Manage, ct);

        if (auth.Failure is not null) return auth.Failure;

        var previous = await _settings.GetAsync("clinic.access.enabled", ct);
        var next = request?.Enabled == true ? "true" : "false";

        var error = await _settings.SetManyAsync(
            new Dictionary<string, string>
            {
                ["clinic.access.enabled"] = next
            },
            auth.Identity!.UserId,
            ct);

        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        await WriteAuditAsync(
            "Clinic", "Clinic",
            next == "true" ? "Enabled Clinic access" : "Disabled Clinic access",
            entityType: "SystemSetting",
            previousStatus: previous,
            newStatus: next,
            reason: request?.Reason,
            ct: ct);

        return Ok(new
        {
            message = next == "true"
                ? "Clinic access is enabled."
                : "Clinic access is disabled.",
            enabled = next == "true"
        });
    }

    // ---------------------------------------------------------
    // AUDIT (deduped page views)
    // ---------------------------------------------------------

    private async Task RecordServiceViewAsync(
        AdminIdentity identity,
        string module,
        string label,
        CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddMinutes(-30).AddHours(8);

        var alreadyLogged = await _db.AdminActivityLogs
            .AsNoTracking()
            .AnyAsync(x =>
                x.UserId == identity.UserId &&
                x.Module == module &&
                x.Action == $"Viewed the {label} service" &&
                x.Timestamp >= since, ct);

        if (alreadyLogged) return;

        await WriteAuditAsync(
            module, "Administrator Activities",
            $"Viewed the {label} service",
            entityType: "Service",
            details: "Opened the central service oversight page.",
            ct: ct);
    }
}

public sealed record ServiceToggleRequest(bool Enabled, string? Reason);
