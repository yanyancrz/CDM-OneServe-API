namespace CDM_OneServe_API.Services.Admin.Library;

// The circulation desk works on Philippine wall-clock time (UTC+8), even when
// the API runs on a UTC host.
internal static class LibraryClock
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

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tz);

    public static DateTime TodayAt(int hour) => Now.Date.AddHours(hour);
}
