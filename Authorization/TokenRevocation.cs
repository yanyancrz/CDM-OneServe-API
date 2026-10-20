using CDM_OneServe_API.Models;

namespace CDM_OneServe_API.Authorization;

/// <summary>
/// Stamps <see cref="User.TokensValidFrom"/> so previously issued JWTs
/// stop satisfying the revocation check in Program.cs.
///
/// For the signed-in user's own password/email change, pass the current
/// token's iat: the stamp is placed one second before it, which revokes
/// every token issued in an earlier second while leaving this device's
/// token usable. A JWT's iat only has second precision, so tokens minted
/// in the same second as the current one cannot be told apart and survive.
/// That is the unavoidable limit of a stateless token.
///
/// For an admin-initiated change (or a password reset with no signed-in
/// session), pass null: the account is stamped with "now" and all of its
/// existing tokens are revoked.
/// </summary>
public static class TokenRevocation
{
    public static void Stamp(User user, DateTime? currentTokenIssuedAt)
    {
        if (user is null)
        {
            return;
        }

        user.TokensValidFrom = currentTokenIssuedAt.HasValue
            ? currentTokenIssuedAt.Value.AddSeconds(-1)
            : DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
    }
}
