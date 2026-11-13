using System.Security.Claims;
using System.Text;

using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Platform;

/// <summary>
/// Central audit writer and reader.
///
/// Writes into the EXISTING <c>admin_activity_logs</c> table, so Academy
/// Library and Lost &amp; Found rows appear in the same central trail without
/// any data migration. New central rows use these conventions:
///
///   Module   - "System", "Users", "Roles", "Library", "Clinic",
///              "Guidance", "LostFound", "Security", "Settings", "Backup"
///   Category - coarse filter: "Overall System", "User Management",
///              "Role and Permission Management", "Security",
///              "System Configuration", "Administrator Activities", ...
///
/// Timestamps are PH wall clock (UTC+8), the same convention
/// LostFoundAuditService already uses, so the central page shows one clock.
///
/// NOTHING here may store a password, token, connection string, medical
/// record or counseling narrative. <see cref="Sanitize"/> is applied to
/// every free-text field before it reaches the table.
/// </summary>
public class AdminAuditService
{
    private const int MaxText = 400;

    private readonly AppDbContext _db;

    public AdminAuditService(AppDbContext db) => _db = db;

    // ---------------------------------------------------------
    // WRITE
    // ---------------------------------------------------------

    /// <summary>
    /// Adds a row and commits it. Use this for standalone actions.
    /// Returns the row so callers can also expose its id.
    /// </summary>
    public async Task<AdminActivityLog> WriteAsync(
        ClaimsPrincipal actor,
        string module,
        string category,
        string action,
        string? entityType = null,
        int? entityId = null,
        string? previousStatus = null,
        string? newStatus = null,
        string? details = null,
        string? reason = null,
        string? previousValue = null,
        string? newValue = null,
        CancellationToken ct = default)
    {
        var row = await StageAsync(
            actor, module, category, action, entityType, entityId,
            previousStatus, newStatus, details, reason, previousValue, newValue, ct);

        await _db.SaveChangesAsync(ct);
        return row;
    }

    /// <summary>
    /// Adds a row WITHOUT saving, so the caller can commit the business change
    /// and its audit entry together.
    /// </summary>
    public async Task<AdminActivityLog> StageAsync(
        ClaimsPrincipal actor,
        string module,
        string category,
        string action,
        string? entityType = null,
        int? entityId = null,
        string? previousStatus = null,
        string? newStatus = null,
        string? details = null,
        string? reason = null,
        string? previousValue = null,
        string? newValue = null,
        CancellationToken ct = default)
    {
        var userId = int.TryParse(
            actor.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id
            : 0;

        var snapshot = await LoadActorAsync(userId, ct);

        var row = new AdminActivityLog
        {
            UserId = userId,
            Action = Sanitize(action, 200) ?? "",
            Timestamp = DateTime.UtcNow.AddHours(8),
            Module = Sanitize(module, 40),
            Category = Sanitize(category, 60),
            ActorName = snapshot.FullName,
            ActorRole = snapshot.Role,
            EntityType = Sanitize(entityType, 60),
            EntityId = entityId,
            PreviousStatus = Sanitize(previousStatus, 60),
            NewStatus = Sanitize(newStatus, 60),
            Details = Sanitize(details, MaxText),
            Reason = Sanitize(reason, MaxText),
            PreviousValue = Sanitize(previousValue, MaxText),
            NewValue = Sanitize(newValue, MaxText),
            IpAddress = Sanitize(snapshot.IpAddress, 64),
            SessionKey = Sanitize(snapshot.SessionKey, 80),
            CorrelationId = Guid.NewGuid().ToString("N")
        };

        _db.AdminActivityLogs.Add(row);
        return row;
    }

    /// <summary>Commits rows staged with <see cref="StageAsync"/>.</summary>
    public Task SaveAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);

    private async Task<(string FullName, string? Role, string? IpAddress, string? SessionKey)>
        LoadActorAsync(int userId, CancellationToken ct)
    {
        if (userId <= 0) return ("Unknown administrator", null, null, null);

        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FullName, u.Role })
            .FirstOrDefaultAsync(ct);

        return (
            string.IsNullOrWhiteSpace(user?.FullName) ? "Unknown administrator" : user!.FullName.Trim(),
            user?.Role,
            null,
            null);
    }

    /// <summary>
    /// Stores request metadata on a staged row. The caller supplies the
    /// HttpContext, so this service never depends on IHttpContextAccessor.
    /// </summary>
    public static void AttachRequest(AdminActivityLog row, HttpContext? http)
    {
        if (http is null) return;

        var forwarded = http.Request.Headers["X-Forwarded-For"].ToString();

        row.IpAddress = Sanitize(
            !string.IsNullOrWhiteSpace(forwarded)
                ? forwarded.Split(',')[0].Trim()
                : http.Connection.RemoteIpAddress?.ToString(),
            64);

        row.SessionKey = Sanitize(
            http.Request.Headers["X-Session-Key"].ToString(), 80);
    }

    /// <summary>
    /// Removes anything that looks like a secret and caps the length.
    /// The audit log is readable by every platform admin, so a careless
    /// message must never leak a credential into it.
    /// </summary>
    public static string? Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = value.Trim();

        foreach (var marker in new[]
                 {
                     "password", "passwd", "pwd", "token", "secret",
                     "connection string", "connectionstring", "apikey",
                     "api key", "x-integration-key"
                 })
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

            if (index >= 0)
            {
                text = text[..index] + "[redacted]";
                break;
            }
        }

        // Collapse control characters so a pasted stack trace cannot break
        // the table layout or smuggle a newline into an export cell.
        var builder = new StringBuilder(text.Length);

        foreach (var ch in text)
        {
            builder.Append(char.IsControl(ch) ? ' ' : ch);
        }

        var clean = builder.ToString().Trim();

        return clean.Length <= maxLength
            ? clean
            : clean[..maxLength];
    }

    // ---------------------------------------------------------
    // READ
    // ---------------------------------------------------------

    public async Task<PagedAuditLogs> QueryAsync(
        AuditLogQuery query,
        CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 10, 100);

        var rows = _db.AdminActivityLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Module))
        {
            var module = query.Module.Trim();
            rows = rows.Where(x => x.Module == module);
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim();
            rows = rows.Where(x => x.Category == category);
        }

        if (query.UserId is { } userId)
        {
            rows = rows.Where(x => x.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var action = query.Action.Trim();
            rows = rows.Where(x => x.Action.Contains(action));
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            var entity = query.EntityType.Trim();
            rows = rows.Where(x => x.EntityType == entity);
        }

        if (query.EntityId is { } entityId)
        {
            rows = rows.Where(x => x.EntityId == entityId);
        }

        if (query.From is { } from)
        {
            rows = rows.Where(x => x.Timestamp >= from);
        }

        if (query.To is { } to)
        {
            rows = rows.Where(x => x.Timestamp <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(x =>
                x.Action.Contains(term) ||
                (x.ActorName != null && x.ActorName.Contains(term)) ||
                (x.Details != null && x.Details.Contains(term)) ||
                (x.EntityType != null && x.EntityType.Contains(term)));
        }

        var total = await rows.CountAsync(ct);

        var items = await rows
            .OrderByDescending(x => x.Timestamp)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AuditLogItem(
                x.Id,
                x.UserId,
                x.ActorName,
                x.ActorRole,
                x.Module,
                x.Category,
                x.Action,
                x.EntityType,
                x.EntityId,
                x.PreviousStatus,
                x.NewStatus,
                x.Details,
                x.Reason,
                x.PreviousValue,
                x.NewValue,
                x.IpAddress,
                x.SessionKey,
                x.CorrelationId,
                x.Timestamp))
            .ToListAsync(ct);

        var totalPages = total == 0
            ? 0
            : (int)Math.Ceiling(total / (double)pageSize);

        return new PagedAuditLogs(items, page, pageSize, total, totalPages);
    }

    public async Task<AuditLogItem?> GetAsync(long id, CancellationToken ct = default)
    {
        return await _db.AdminActivityLogs
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new AuditLogItem(
                x.Id,
                x.UserId,
                x.ActorName,
                x.ActorRole,
                x.Module,
                x.Category,
                x.Action,
                x.EntityType,
                x.EntityId,
                x.PreviousStatus,
                x.NewStatus,
                x.Details,
                x.Reason,
                x.PreviousValue,
                x.NewValue,
                x.IpAddress,
                x.SessionKey,
                x.CorrelationId,
                x.Timestamp))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AuditFacets> GetFacetsAsync(CancellationToken ct = default)
    {
        var modules = await _db.AdminActivityLogs.AsNoTracking()
            .Where(x => x.Module != null)
            .Select(x => x.Module!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

        var categories = await _db.AdminActivityLogs.AsNoTracking()
            .Where(x => x.Category != null)
            .Select(x => x.Category!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);

        var actions = await _db.AdminActivityLogs.AsNoTracking()
            .Select(x => x.Action)
            .Distinct()
            .OrderBy(x => x)
            .Take(200)
            .ToListAsync(ct);

        return new AuditFacets(modules, categories, actions);
    }

    /// <summary>
    /// Streams the filtered trail as CSV. The same filters the page uses, so
    /// an export always matches what the admin is looking at.
    /// </summary>
    public async Task<string> ExportCsvAsync(
        AuditLogQuery query,
        CancellationToken ct = default)
    {
        query = query with { Page = 1, PageSize = 100 };

        var builder = new StringBuilder();
        builder.AppendLine(
            "Id,Timestamp,Actor,Role,Module,Category,Action,Entity,EntityId,Previous,New,Details,Reason,IpAddress");

        // Bounded: 100 rows per page, 50 pages = 5000 rows. A full-table
        // export would be a memory risk on a small App Service plan.
        for (var page = 1; page <= 50; page++)
        {
            var result = await QueryAsync(query with { Page = page }, ct);

            if (result.Items.Count == 0) break;

            foreach (var item in result.Items)
            {
                builder.AppendLine(string.Join(",",
                    item.Id,
                    item.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                    Csv(item.ActorName),
                    Csv(item.ActorRole),
                    Csv(item.Module),
                    Csv(item.Category),
                    Csv(item.Action),
                    Csv(item.EntityType),
                    item.EntityId?.ToString() ?? "",
                    Csv(item.PreviousStatus),
                    Csv(item.NewStatus),
                    Csv(item.Details),
                    Csv(item.Reason),
                    Csv(item.IpAddress)));

                if (page == 50 && result.Items.Count == result.PageSize) break;
            }

            if (result.Page >= result.TotalPages) break;
        }

        return builder.ToString();
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        var escaped = value.Replace("\"", "\"\"");

        return escaped.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? $"\"{escaped}\""
            : escaped;
    }
}

public sealed record AuditLogQuery(
    string? Module = null,
    string? Category = null,
    int? UserId = null,
    string? Action = null,
    string? EntityType = null,
    int? EntityId = null,
    DateTime? From = null,
    DateTime? To = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

public sealed record AuditLogItem(
    long Id,
    int UserId,
    string? ActorName,
    string? ActorRole,
    string? Module,
    string? Category,
    string Action,
    string? EntityType,
    int? EntityId,
    string? PreviousStatus,
    string? NewStatus,
    string? Details,
    string? Reason,
    string? PreviousValue,
    string? NewValue,
    string? IpAddress,
    string? SessionKey,
    string? CorrelationId,
    DateTime Timestamp)
{
    /// <summary>Same instant as UTC, for clients that format locally.</summary>
    public DateTime TimestampUtc => Timestamp.AddHours(-8);
}

public sealed record PagedAuditLogs(
    List<AuditLogItem> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record AuditFacets(
    List<string> Modules,
    List<string> Categories,
    List<string> Actions);
