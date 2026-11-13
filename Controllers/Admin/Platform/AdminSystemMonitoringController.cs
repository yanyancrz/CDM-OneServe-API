using CDM_OneServe_API.Models.Platform;
using CDM_OneServe_API.Services.Admin.Platform;

using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin.Platform;

/// <summary>
/// System monitoring (Part 11). Every component status comes from a real
/// probe in <see cref="SystemHealthService"/> - no hard-coded "online".
/// </summary>
[Route("api/admin/system-monitoring")]
public class AdminSystemMonitoringController : AdminPlatformControllerBase
{
    private readonly SystemHealthService _health;

    public AdminSystemMonitoringController(
        SystemHealthService health,
        PermissionService permissions,
        AdminAuditService audit,
        SecurityEventService security)
        : base(permissions, audit, security)
    {
        _health = health;
    }

    // GET: api/admin/system-monitoring
    [HttpGet]
    public async Task<IActionResult> GetLive(CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.MonitoringView, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var report = await _health.CheckAllAsync(ct);

        return Ok(new
        {
            overallStatus = report.OverallStatus,
            checkedAt = report.CheckedAt,
            components = report.Components
        });
    }

    // GET: api/admin/system-monitoring/history
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] string? component,
        [FromQuery] int hours = 24,
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.MonitoringView, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var rows = await _health.GetHistoryAsync(component, hours, ct);

        return Ok(new
        {
            component = component ?? "all",
            hours = Math.Clamp(hours, 1, 168),
            count = rows.Count,
            items = rows
        });
    }

    // POST: api/admin/system-monitoring/check
    /// <summary>Runs the probes and stores the result in the history.</summary>
    [HttpPost("check")]
    public async Task<IActionResult> RecordCheck(CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.MonitoringView, PermissionLevels.Manage, ct);

        if (auth.Failure is not null) return auth.Failure;

        var report = await _health.CheckAndRecordAsync(ct);

        await WriteAuditAsync(
            "System", "Overall System",
            "Recorded a system health check",
            entityType: "HealthCheck",
            details: $"Overall status: {report.OverallStatus}.",
            ct: ct);

        return Ok(new
        {
            overallStatus = report.OverallStatus,
            checkedAt = report.CheckedAt,
            components = report.Components
        });
    }
}
