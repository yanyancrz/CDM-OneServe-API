using CDM_OneServe_API.Models.Platform;
using CDM_OneServe_API.Services.Admin.Platform;

using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin.Platform;

/// <summary>
/// System settings (Part 13).
///
/// Only keys in <see cref="SystemSettingsService.Catalog"/> can be written,
/// every value is type-checked, and a batch with any invalid value writes
/// nothing. Settings marked "requires confirmation" cannot be changed
/// without an explicit <c>confirm: true</c> from the UI, and every changed
/// key is audited with its old and new value.
/// </summary>
[Route("api/admin/settings")]
public class AdminSystemSettingsController : AdminPlatformControllerBase
{
    private const string Module = "Settings";
    private const string Category = "System Configuration";

    private readonly SystemSettingsService _settings;

    public AdminSystemSettingsController(
        SystemSettingsService settings,
        PermissionService permissions,
        AdminAuditService audit,
        SecurityEventService security)
        : base(permissions, audit, security)
    {
        _settings = settings;
    }

    // GET: api/admin/settings
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.SettingsManage, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var items = await _settings.GetAllAsync(ct);

        return Ok(new
        {
            settings = items,
            categories = items
                .Select(x => x.Category)
                .Distinct()
                .OrderBy(x => x)
                .ToList(),
            canEdit = auth.Identity!.Can(
                PermissionCatalog.SettingsManage, PermissionLevels.Manage)
        });
    }

    // PUT: api/admin/settings
    [HttpPut]
    public async Task<IActionResult> Update(
        [FromBody] SettingsUpdateRequest request,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.SettingsManage, PermissionLevels.Manage, ct);

        if (auth.Failure is not null) return auth.Failure;

        if (request?.Values is null || request.Values.Count == 0)
        {
            return BadRequest(new { message = "No settings were supplied." });
        }

        var current = await _settings.GetAllAsync(ct);
        var currentMap = current.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

        var changed = request.Values
            .Where(kv => currentMap.TryGetValue(kv.Key, out var old) && old != kv.Value)
            .Select(kv => new
            {
                kv.Key,
                Old = currentMap[kv.Key],
                New = kv.Value
            })
            .ToList();

        if (changed.Count == 0)
        {
            return Ok(new { message = "Nothing changed.", changed = 0 });
        }

        var needsConfirmation = changed
            .Select(x => SystemSettingsService.Find(x.Key))
            .Any(x => x is { RequiresConfirmation: true });

        if (needsConfirmation && !request.Confirm)
        {
            return BadRequest(new
            {
                message = "This change needs explicit confirmation. Re-send with confirm=true.",
                requiresConfirmation = true
            });
        }

        var error = await _settings.SetManyAsync(
            request.Values, auth.Identity!.UserId, ct);

        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        foreach (var change in changed)
        {
            var definition = SystemSettingsService.Find(change.Key);

            await WriteAuditAsync(
                Module, Category,
                $"Changed setting: {definition?.Label ?? change.Key}",
                entityType: "SystemSetting",
                details: request.Reason,
                reason: request.Reason,
                previousValue: change.Old,
                newValue: change.New,
                ct: ct);
        }

        return Ok(new
        {
            message = $"Saved {changed.Count} setting(s).",
            changed = changed.Count
        });
    }
}

public sealed record SettingsUpdateRequest(
    Dictionary<string, string> Values,
    bool Confirm,
    string? Reason);
