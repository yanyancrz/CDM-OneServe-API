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
}

public class UserActivity
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Action { get; set; } = "";

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}