namespace CDM_OneServe_API.Services;

/// <summary>
/// Timezone helpers for values coming out of zone-less MySQL `datetime`
/// columns.
///
/// EF Core / MySqlConnector materialise those columns as
/// <see cref="DateTimeKind.Unspecified"/>, and System.Text.Json then writes
/// them without a "Z" or offset. A browser parses such a string as *local*
/// time, so a UTC value displays eight hours early in the Philippines.
///
/// OneServe's own tables (UserSessions, UserActivities, Notifications written
/// by Guidance/Library) store UTC wall clocks. Lost &amp; Found deliberately
/// stores Philippine wall clocks (see <c>LostFoundService.PhNow</c> and
/// Database/LostFound/003_ph_wall_clock.sql), so it needs the other helper.
///
/// These convert the *reading* side only. They never rewrite stored rows.
/// </summary>
public static class UtcTimestamps
{
    /// <summary>The Philippines has no daylight saving; UTC+8 all year.</summary>
    private static readonly TimeSpan PhOffset = TimeSpan.FromHours(8);

    /// <summary>
    /// A UTC wall clock read from MySQL -> the same instant marked as UTC.
    /// System.Text.Json will serialise it with a trailing "Z".
    /// </summary>
    public static DateTime FromDatabase(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    public static DateTime? FromDatabase(DateTime? value) =>
        value is null ? null : FromDatabase(value.Value);

    /// <summary>
    /// A Philippine wall clock read from MySQL -> the matching UTC instant.
    /// Used only for the Lost &amp; Found rows mirrored into the global
    /// notifications table; their stored value is already +08:00.
    /// </summary>
    public static DateTime FromPhWallClock(DateTime value) =>
        DateTime.SpecifyKind(
            DateTime.SpecifyKind(value, DateTimeKind.Unspecified) - PhOffset,
            DateTimeKind.Utc);
}
