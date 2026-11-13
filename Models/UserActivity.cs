namespace CDM_OneServe_API.Models;

/// <summary>
/// Well-known <see cref="UserActivity.Action"/> values. Anything that needs to
/// find rows written by the app should use these constants, not inline strings.
/// </summary>
public static class UserActivityActions
{
    /// <summary>
    /// Written on every successful login. The Guidance Head reports read
    /// MAX(CreatedAt) for this action as the account's "Last login".
    /// </summary>
    public const string Login = "Logged in";

    /// <summary>
    /// Written once per student the first time the Clinic module returns at
    /// least one medical record for them. The clinic itself lives in a
    /// separate CMS database, so this local marker is how the admin dashboard
    /// counts students who have actually visited the clinic.
    /// </summary>
    public const string ClinicRecordsViewed = "Viewed clinic records";
}

public class UserActivity
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Action { get; set; } = "";

    public string? Description { get; set; }

    // UTC. Guidance Head reads MAX(CreatedAt) as "last login", so this value
    // is surfaced directly to the admin UI.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}