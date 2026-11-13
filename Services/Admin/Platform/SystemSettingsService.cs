using System.Text.Json;

using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CDM_OneServe_API.Services.Admin.Platform;

/// <summary>
/// One platform setting the admin can change.
/// </summary>
/// <param name="Key">Stable key; unknown keys are rejected on write.</param>
/// <param name="Value">Current value serialised as text.</param>
/// <param name="ValueType">"string", "bool" or "int".</param>
/// <param name="Category">Grouping on the settings page.</param>
/// <param name="Label">Human label.</param>
/// <param name="Description">What changing it does.</param>
/// <param name="DefaultValue">Value before anyone edits it.</param>
/// <param name="RequiresConfirmation">UI must show a confirm dialog.</param>
public sealed record SettingDefinition(
    string Key,
    string Value,
    string ValueType,
    string Category,
    string Label,
    string Description,
    string DefaultValue,
    bool RequiresConfirmation = false);

/// <summary>
/// Typed, validated platform settings.
///
/// Reads are cached for 30 seconds because the maintenance-mode middleware
/// runs on every request; a write invalidates the cache immediately. Values
/// are never cached across a failed write, so an admin never sees a setting
/// that did not persist.
/// </summary>
public class SystemSettingsService
{
    private const string CacheKey = "platform-settings-v1";

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public SystemSettingsService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    /// <summary>
    /// The settings this build understands, with factory defaults.
    /// Adding a new setting means adding it here - a row in the database
    /// alone is never read by anything.
    /// </summary>
    public static readonly IReadOnlyList<SettingDefinition> Catalog = new List<SettingDefinition>
    {
        new("platform.name", "CDM OneServe", "string", "General",
            "Platform name", "Shown in the admin header and page titles.", "CDM OneServe"),

        new("platform.supportEmail", "cdmoneserve@gmail.com", "string", "General",
            "Support email", "Where users are told to write for help.", "cdmoneserve@gmail.com"),

        new("platform.maintenanceMode", "false", "bool", "Maintenance",
            "Maintenance mode",
            "When on, student/faculty APIs answer 503 with the maintenance message. Admin APIs keep working.",
            "false", RequiresConfirmation: true),

        new("platform.maintenanceMessage",
            "CDM OneServe is under maintenance. Please try again shortly.",
            "string", "Maintenance",
            "Maintenance message", "Shown to users while maintenance mode is on.",
            "CDM OneServe is under maintenance. Please try again shortly."),

        new("modules.library.enabled", "true", "bool", "Modules",
            "Library access", "Turn the Library module on or off for users.",
            "true", RequiresConfirmation: true),

        new("modules.clinic.enabled", "true", "bool", "Modules",
            "Clinic access", "Turn the Clinic module on or off for users.",
            "true", RequiresConfirmation: true),

        new("modules.guidance.enabled", "true", "bool", "Modules",
            "Guidance access", "Turn the Guidance module on or off for users.",
            "true", RequiresConfirmation: true),

        new("modules.lostfound.enabled", "true", "bool", "Modules",
            "Lost & Found access", "Turn the Lost & Found module on or off for users.",
            "true", RequiresConfirmation: true),

        new("security.lockout.enabled", "false", "bool", "Security",
            "Lock accounts after failed sign-ins",
            "Off by default so existing accounts behave exactly as before. When on, repeated wrong passwords temporarily lock the account.",
            "false", RequiresConfirmation: true),

        new("security.lockout.maxAttempts", "5", "int", "Security",
            "Attempts before lockout", "Wrong passwords allowed before a lockout. 3-10.",
            "5"),

        new("security.lockout.minutes", "15", "int", "Security",
            "Lockout length (minutes)", "How long a locked account stays locked. 5-1440.",
            "15"),

        new("security.alertOnNewDevice", "true", "bool", "Security",
            "Flag new-device sign-ins",
            "Records a security event when an account signs in from a device it has not used before.",
            "true"),

        new("notifications.pushEnabled", "true", "bool", "Notifications",
            "Web Push", "Allow OneServe to queue browser push notifications.",
            "true"),

        new("notifications.announcementPush", "true", "bool", "Notifications",
            "Announcement push", "Broadcast a push when an announcement is published.",
            "true"),

        new("clinic.access.enabled", "true", "bool", "Modules",
            "Clinic CMS access", "Allow OneServe to proxy Clinic CMS requests for students.",
            "true", RequiresConfirmation: true),

        new("backup.retentionDays", "30", "int", "Backup",
            "Backup retention (days)", "How long the operator should retain database backups. 7-365.",
            "30"),
    };

    public static SettingDefinition? Find(string key) =>
        Catalog.FirstOrDefault(x => x.Key == key);

    /// <summary>All settings, database values merged over the defaults.</summary>
    public async Task<List<SettingDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        var stored = await LoadAsync(ct);

        return Catalog.Select(definition =>
        {
            if (stored.TryGetValue(definition.Key, out var row))
            {
                return definition with { Value = row.Value };
            }

            return definition;
        }).ToList();
    }

    public async Task<string> GetAsync(
        string key,
        CancellationToken ct = default)
    {
        var definition = Find(key);
        var fallback = definition?.DefaultValue ?? "";

        var stored = await LoadAsync(ct);

        return stored.TryGetValue(key, out var row) ? row.Value : fallback;
    }

    public async Task<bool> GetBoolAsync(string key, CancellationToken ct = default)
    {
        var raw = await GetAsync(key, ct);
        return bool.TryParse(raw, out var value) ? value : false;
    }

    public int GetInt(string key, int fallback)
    {
        var raw = Get(key);
        return int.TryParse(raw, out var value) ? value : fallback;
    }

    /// <summary>Synchronous read for middleware, served from the cache.</summary>
    public string Get(string key)
    {
        var definition = Find(key);
        var fallback = definition?.DefaultValue ?? "";

        if (!_cache.TryGetValue(CacheKey, out Dictionary<string, StoredSetting>? stored) ||
            stored is null)
        {
            stored = LoadSync();
        }

        return stored.TryGetValue(key, out var row) ? row.Value : fallback;
    }

    public bool GetBool(string key) =>
        bool.TryParse(Get(key), out var value) && value;

    /// <summary>
    /// Validates and writes a batch. Every key must be known and every value
    /// must parse, otherwise nothing is written - a partially applied settings
    /// change is worse than a rejected one.
    /// </summary>
    public async Task<string?> SetManyAsync(
        IReadOnlyDictionary<string, string> values,
        int actorUserId,
        CancellationToken ct = default)
    {
        if (values.Count == 0) return "No settings were supplied.";

        var validated = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, rawValue) in values)
        {
            var definition = Find(key);

            if (definition is null) return $"Unknown setting '{key}'.";

            var value = (rawValue ?? "").Trim();

            switch (definition.ValueType)
            {
                case "bool":
                    if (!bool.TryParse(value, out var flag))
                        return $"'{definition.Label}' must be true or false.";
                    value = flag ? "true" : "false";
                    break;

                case "int":
                    if (!int.TryParse(value, out var number))
                        return $"'{definition.Label}' must be a whole number.";

                    var (min, max) = key switch
                    {
                        "security.lockout.maxAttempts" => (3, 10),
                        "security.lockout.minutes" => (5, 1440),
                        "backup.retentionDays" => (7, 365),
                        _ => (0, int.MaxValue)
                    };

                    if (number < min || number > max)
                        return $"'{definition.Label}' must be between {min} and {max}.";

                    value = number.ToString();
                    break;

                case "string":
                    if (value.Length > 500)
                        return $"'{definition.Label}' is too long (max 500 characters).";
                    break;
            }

            validated[key] = value;
        }

        var now = DateTime.UtcNow;

        var existing = await _db.SystemSettings
            .Where(x => validated.Keys.Contains(x.Key))
            .ToListAsync(ct);

        foreach (var (key, value) in validated)
        {
            var definition = Find(key)!;
            var row = existing.FirstOrDefault(x => x.Key == key);

            if (row is null)
            {
                row = new SystemSetting
                {
                    Key = key,
                    Category = definition.Category,
                    ValueType = definition.ValueType,
                    Description = definition.Description
                };
                _db.SystemSettings.Add(row);
            }

            row.Value = value;
            row.UpdatedByUserId = actorUserId;
            row.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);

        _cache.Remove(CacheKey);
        return null;
    }

    // ---------------------------------------------------------
    // LOAD / CACHE
    // ---------------------------------------------------------

    private async Task<Dictionary<string, StoredSetting>> LoadAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out Dictionary<string, StoredSetting>? cached) &&
            cached is not null)
        {
            return cached;
        }

        var rows = await _db.SystemSettings
            .AsNoTracking()
            .Select(x => new StoredSetting(x.Key, x.Value))
            .ToListAsync(ct);

        var map = rows.ToDictionary(x => x.Key, x => x, StringComparer.Ordinal);

        _cache.Set(CacheKey, map, TimeSpan.FromSeconds(30));
        return map;
    }

    private Dictionary<string, StoredSetting> LoadSync()
    {
        var rows = _db.SystemSettings
            .AsNoTracking()
            .Select(x => new StoredSetting(x.Key, x.Value))
            .ToList();

        var map = rows.ToDictionary(x => x.Key, x => x, StringComparer.Ordinal);

        _cache.Set(CacheKey, map, TimeSpan.FromSeconds(15));
        return map;
    }

    internal sealed record StoredSetting(string Key, string Value);
}
