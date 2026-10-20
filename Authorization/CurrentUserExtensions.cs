using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace CDM_OneServe_API.Authorization;

/// <summary>
/// Shared helpers for reading the authenticated identity from the JWT.
/// Controllers must derive authorization decisions from these claims and
/// never from a userId/email supplied in the request body or query string.
/// </summary>
public static class CurrentUserExtensions
{
    public static int? GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(value, out var id) && id > 0
            ? id
            : null;
    }

    public static bool TryGetUserId(this ClaimsPrincipal user, out int userId)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(value, out userId) && userId > 0;
    }

    public static string GetRole(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Role) ?? "";

    /// <summary>
    /// The current JWT's issued-at (iat) claim, read as UTC. Used by
    /// <see cref="TokenRevocation"/> to keep the caller's own token alive
    /// while revoking the ones issued before it.
    /// </summary>
    public static DateTime? GetTokenIssuedAt(this ClaimsPrincipal user)
    {
        var raw = user.FindFirst("iat")?.Value
            ?? user.FindFirst(JwtRegisteredClaimNames.Iat)?.Value;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (long.TryParse(raw, out var epoch))
        {
            return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
        }

        if (DateTime.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal |
                    DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public static bool IsPlatformAdmin(this ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.IsInRole("SuperAdmin");

    public static bool IsLibraryStaff(this ClaimsPrincipal user) =>
        user.IsInRole("LibraryAdmin") ||
        user.IsInRole("LibraryStaff") ||
        user.IsInRole("SuperAdmin");
}
