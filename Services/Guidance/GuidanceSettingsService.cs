using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models.Guidance;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Guidance;

/// <summary>
/// The Guidance module's own settings, stored as rows in guidance_settings
/// (Key / Value). Every value has a default that reproduces the original,
/// hard-coded behaviour, so a brand new install behaves exactly as before
/// until somebody opens the Guidance Head Settings page.
/// </summary>
public class GuidanceModuleSettings
{
    /// <summary>Days after the appointment date before an unanswered Pending request expires. 0 = expire the next day.</summary>
    public int AutoExpireDays { get; set; }

    /// <summary>Length of one appointment slot, in minutes.</summary>
    public int SlotMinutes { get; set; } = 30;

    /// <summary>How many days ahead a student may book.</summary>
    public int MaxAdvanceDays { get; set; } = 60;

    /// <summary>Students may book for today.</summary>
    public bool AllowSameDay { get; set; } = true;
}

/// <summary>
/// Reads and writes <see cref="GuidanceModuleSettings"/>. Reads are cheap
/// (the table is a handful of rows) and always fall back to the defaults, so a
/// missing table or a bad value can never break booking or scheduling.
/// </summary>
public class GuidanceSettingsService
{
    public const string KeyAutoExpireDays = "AutoExpireDays";
    public const string KeySlotMinutes = "SlotMinutes";
    public const string KeyMaxAdvanceDays = "MaxAdvanceDays";
    public const string KeyAllowSameDay = "AllowSameDay";

    private const int MinSlotMinutes = 5;
    private const int MaxSlotMinutes = 240;

    private readonly AppDbContext _context;

    public GuidanceSettingsService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<GuidanceModuleSettings> GetAsync(CancellationToken ct = default)
    {
        var settings = new GuidanceModuleSettings();

        try
        {
            var rows = await _context.GuidanceSettings
                .AsNoTracking()
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                switch (row.Key)
                {
                    case KeyAutoExpireDays:
                        settings.AutoExpireDays = Clamp(ParseInt(row.Value) ?? 0, 0, 90);
                        break;
                    case KeySlotMinutes:
                        settings.SlotMinutes = Clamp(ParseInt(row.Value) ?? 30, MinSlotMinutes, MaxSlotMinutes);
                        break;
                    case KeyMaxAdvanceDays:
                        settings.MaxAdvanceDays = Clamp(ParseInt(row.Value) ?? 60, 1, 365);
                        break;
                    case KeyAllowSameDay:
                        settings.AllowSameDay = ParseBool(row.Value, true);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Table not created yet, or a bad row - the defaults already match
            // the behaviour the module shipped with.
        }

        return settings;
    }

    public async Task<GuidanceModuleSettings> SaveAsync(
        GuidanceModuleSettings settings,
        int? updatedBy,
        CancellationToken ct = default)
    {
        var clean = new GuidanceModuleSettings
        {
            AutoExpireDays = Clamp(settings.AutoExpireDays, 0, 90),
            SlotMinutes = Clamp(settings.SlotMinutes, MinSlotMinutes, MaxSlotMinutes),
            MaxAdvanceDays = Clamp(settings.MaxAdvanceDays, 1, 365),
            AllowSameDay = settings.AllowSameDay
        };

        var values = new Dictionary<string, string?>
        {
            [KeyAutoExpireDays] = clean.AutoExpireDays.ToString(),
            [KeySlotMinutes] = clean.SlotMinutes.ToString(),
            [KeyMaxAdvanceDays] = clean.MaxAdvanceDays.ToString(),
            [KeyAllowSameDay] = clean.AllowSameDay ? "true" : "false"
        };

        var existing = await _context.GuidanceSettings.ToListAsync(ct);

        foreach (var (key, value) in values)
        {
            var row = existing.FirstOrDefault(r => r.Key == key);

            if (row == null)
            {
                _context.GuidanceSettings.Add(new GuidanceSetting
                {
                    Key = key,
                    Value = value,
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedBy = updatedBy
                });
            }
            else
            {
                row.Value = value;
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedBy = updatedBy;
            }
        }

        await _context.SaveChangesAsync(ct);

        return clean;
    }

    private static int? ParseInt(string? value) =>
        int.TryParse(value?.Trim(), out var parsed) ? parsed : null;

    private static bool ParseBool(string? value, bool fallback) =>
        bool.TryParse(value?.Trim(), out var parsed) ? parsed : fallback;

    private static int Clamp(int value, int min, int max) =>
        value < min ? min : value > max ? max : value;
}
