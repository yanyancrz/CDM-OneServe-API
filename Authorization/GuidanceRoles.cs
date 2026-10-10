namespace CDM_OneServe_API.Authorization;

/// <summary>
/// Single source of truth for Guidance Counseling roles.
/// These are the same role strings AuthController writes into the JWT
/// (ClaimTypes.Role). Guidance has NO login/accounts of its own.
/// </summary>
public static class GuidanceRoles
{
    /// <summary>Guidance counselor. Lands on the Counselor dashboard, never on the OneServe dashboard.</summary>
    public const string Counselor = "Counselor";

    public const string Student = "Student";
    public const string Faculty = "Faculty";

    /// <summary>
    /// Guidance Head - the administrator of the Guidance module.
    /// Same "module head" convention as LibraryAdmin (AdminModule = "Guidance").
    /// </summary>
    public const string Head = "GuidanceAdmin";

    /// <summary>Who may open /api/admin/guidance/* (Head, plus the platform admins).</summary>
    public const string HeadAccess = "SuperAdmin,Admin," + Head;

    public static bool IsHead(string? role) =>
        string.Equals(role, Head, StringComparison.Ordinal);

    /// <summary>[Authorize(Roles = GuidanceRoles.StudentSide)] - students (and faculty) using Guidance.</summary>
    public const string StudentSide = Student + "," + Faculty;

    /// <summary>[Authorize(Roles = GuidanceRoles.Any)] - anyone allowed inside the Guidance module.</summary>
    public const string Any = Student + "," + Faculty + "," + Counselor;

    public static bool IsCounselor(string? role) =>
        string.Equals(role, Counselor, StringComparison.Ordinal);
}