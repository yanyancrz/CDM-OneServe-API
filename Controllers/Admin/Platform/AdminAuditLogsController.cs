using System.Text;

using CDM_OneServe_API.Models.Platform;
using CDM_OneServe_API.Services.Admin.Platform;

using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin.Platform;

/// <summary>
/// Central, read-only audit trail.
///
/// The table (<c>admin_activity_logs</c>) is the one Library and Lost &amp;
/// Found already write to, so this page shows every module in one place
/// without a second log. There is deliberately NO update or delete route:
/// audit records are append-only, and only the API writes them.
/// </summary>
[Route("api/admin/audit-logs")]
public class AdminAuditLogsController : AdminPlatformControllerBase
{
    public AdminAuditLogsController(
        PermissionService permissions,
        AdminAuditService audit,
        SecurityEventService security)
        : base(permissions, audit, security)
    {
    }

    // GET: api/admin/audit-logs
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? module,
        [FromQuery] string? category,
        [FromQuery] int? userId,
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] int? entityId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.AuditView, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var query = new AuditLogQuery(
            module, category, userId, action, entityType, entityId,
            from, to, search, page, pageSize);

        var result = await Audit.QueryAsync(query, ct);

        return Ok(new
        {
            items = result.Items,
            page = result.Page,
            pageSize = result.PageSize,
            totalItems = result.TotalItems,
            totalPages = result.TotalPages
        });
    }

    // GET: api/admin/audit-logs/facets
    [HttpGet("facets")]
    public async Task<IActionResult> GetFacets(CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.AuditView, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var facets = await Audit.GetFacetsAsync(ct);

        return Ok(new
        {
            modules = facets.Modules,
            categories = facets.Categories,
            actions = facets.Actions
        });
    }

    // GET: api/admin/audit-logs/{id}
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetOne(long id, CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.AuditView, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var item = await Audit.GetAsync(id, ct);

        return item is null
            ? NotFound(new { message = "Audit record not found." })
            : Ok(item);
    }

    // GET: api/admin/audit-logs/export
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] string? module,
        [FromQuery] string? category,
        [FromQuery] int? userId,
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] int? entityId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? search,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.AuditView, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var query = new AuditLogQuery(
            module, category, userId, action, entityType, entityId,
            from, to, search, 1, 100);

        var csv = await Audit.ExportCsvAsync(query, ct);

        // Exporting the security trail is itself an auditable action.
        await WriteAuditAsync(
            "System", "Overall System", "Exported audit log",
            entityType: "AuditLog",
            details: $"Filtered export ({csv.Length} characters).",
            ct: ct);

        var bytes = Encoding.UTF8.GetBytes(csv);

        return File(
            bytes,
            "text/csv",
            $"oneserve-audit-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }
}
