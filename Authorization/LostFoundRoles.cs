namespace CDM_OneServe_API.Authorization;

/// <summary>
/// Single source of truth for Lost &amp; Found role names.
///
/// The original module owner role is <c>LostFoundAdmin</c>. It is kept as a
/// SUPER-admin alias so existing accounts and existing tokens keep working;
/// new module owners are created as <c>LostFoundSuperAdmin</c>, and the
/// accounts they create are <c>LostFoundSubAdmin</c>.
///
/// OneServe's built-in <c>Admin</c> and <c>SuperAdmin</c> administer every
/// module, so they pass every check too.
/// </summary>
public static class LostFoundRoles
{
    /// <summary>The module owner (new name).</summary>
    public const string SuperAdmin = "LostFoundSuperAdmin";

    /// <summary>A delegated administrator created by the module owner.</summary>
    public const string SubAdmin = "LostFoundSubAdmin";

    /// <summary>Legacy module-owner role. Treated exactly like SuperAdmin.</summary>
    public const string LegacyAdmin = "LostFoundAdmin";

    /// <summary>For [Authorize(Roles = ...)] - every Lost &amp; Found administrator.</summary>
    public const string Access =
        LegacyAdmin + "," + SuperAdmin + "," + SubAdmin + ",Admin,SuperAdmin";

    /// <summary>Administrative actions (management, audit, login monitoring).</summary>
    public const string Management =
        LegacyAdmin + "," + SuperAdmin + ",Admin,SuperAdmin";

    /// <summary>Everything a signed-in sub-admin may do.</summary>
    public const string SubAdminOnly = SubAdmin;

    public static bool IsSuperRole(string? role) =>
        string.Equals(role, SuperAdmin, StringComparison.Ordinal) ||
        string.Equals(role, LegacyAdmin, StringComparison.Ordinal) ||
        string.Equals(role, "Admin", StringComparison.Ordinal) ||
        string.Equals(role, "SuperAdmin", StringComparison.Ordinal);

    public static bool IsSubAdminRole(string? role) =>
        string.Equals(role, SubAdmin, StringComparison.Ordinal);

    public static bool IsLostFoundRole(string? role) =>
        IsSuperRole(role) || IsSubAdminRole(role);

    /// <summary>Human label used in audit rows and on screen.</summary>
    public static string Label(string? role) => role switch
    {
        _ when IsSubAdminRole(role) => "Sub-Admin",
        _ when IsSuperRole(role) => "Super Admin",
        _ => role ?? string.Empty
    };

    /// <summary>
    /// Who did it, in the console's display format:
    /// "Maria Santos (Sub-Admin)" or "Super Admin — Maria Santos".
    /// </summary>
    public static string Attribution(string? fullName, string? role)
    {
        var name = string.IsNullOrWhiteSpace(fullName)
            ? "Unknown administrator"
            : fullName.Trim();

        return IsSuperRole(role)
            ? $"Super Admin — {name}"
            : $"{name} ({Label(role)})";
    }
}
