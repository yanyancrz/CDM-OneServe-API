namespace CDM_OneServe_API.Authorization;

/// <summary>
/// Single source of truth for Library role names.
/// These match the roles AuthController already writes into the JWT
/// (ClaimTypes.Role) - do not add duplicate roles.
/// </summary>
public static class LibraryRoles
{
    /// <summary>Library Head (created through POST /api/auth/heads).</summary>
    public const string Head = "LibraryAdmin";

    /// <summary>Library Staff (created by the Library Head).</summary>
    public const string Staff = "LibraryStaff";

    /// <summary>For [Authorize(Roles = LibraryRoles.Access)] - Head and Staff.</summary>
    public const string Access = Head + "," + Staff;

    /// <summary>For [Authorize(Roles = LibraryRoles.HeadOnly)] - Head only.</summary>
    public const string HeadOnly = Head;

    public static bool IsLibraryRole(string? role) =>
        string.Equals(role, Head, StringComparison.Ordinal) ||
        string.Equals(role, Staff, StringComparison.Ordinal);

    public static string Label(string? role) => role switch
    {
        Head => "Library Head",
        Staff => "Library Staff",
        _ => role ?? string.Empty
    };
}