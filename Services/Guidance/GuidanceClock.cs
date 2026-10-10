using System.Globalization;

namespace CDM_OneServe_API.Services.Guidance;

/// <summary>
/// "Now" / "today" for the school (Philippines, UTC+8) no matter where the
/// server runs. Replaces the Node module's TZ=Asia/Manila setting.
/// </summary>
public static class GuidanceClock
{
    private static readonly TimeZoneInfo Tz = FindZone();

    private static TimeZoneInfo FindZone()
    {
        foreach (var id in new[] { "Asia/Manila", "Singapore Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("PH", TimeSpan.FromHours(8), "PH", "PH");
    }

    public static DateTime Now => TimeZoneInfo.ConvertTime(DateTime.UtcNow, Tz);
    public static DateOnly Today => DateOnly.FromDateTime(Now);
    public static int MinutesNow => Now.Hour * 60 + Now.Minute;

    /// <summary>
    /// A stored UTC instant -> naive Philippine wall-clock time.
    /// Guidance tables use DateTime.UtcNow; the UIs show local time, so every
    /// UTC value must pass through here before it reaches a response.
    /// </summary>
    public static DateTime FromUtc(DateTime value) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(value, DateTimeKind.Utc), Tz);

    /// <summary>"10:00 AM" / "2:30 PM" -> minutes since midnight, or null if not that format.</summary>
    public static int? ParseSlot(string? slot)
    {
        if (string.IsNullOrWhiteSpace(slot)) return null;
        var parts = slot.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return null;

        var hm = parts[0].Split(':');
        if (hm.Length != 2 ||
            !int.TryParse(hm[0], out var h) || !int.TryParse(hm[1], out var m) ||
            h < 1 || h > 12 || m < 0 || m > 59) return null;

        var ampm = parts[1].ToUpperInvariant();
        if (ampm != "AM" && ampm != "PM") return null;

        if (ampm == "PM" && h != 12) h += 12;
        if (ampm == "AM" && h == 12) h = 0;
        return h * 60 + m;
    }

    /// <summary>Minutes since midnight -> "9:30 AM".</summary>
    public static string FormatSlot(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        var ampm = h < 12 ? "AM" : "PM";
        var h12 = h == 0 ? 12 : h > 12 ? h - 12 : h;
        return string.Create(CultureInfo.InvariantCulture, $"{h12}:{m:00} {ampm}");
    }
}