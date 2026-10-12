using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.IdentityModel.Tokens;

namespace CDM_OneServe_API.Services;

/// <summary>
/// Verifies the Google ID token that the Google Identity
/// Services button hands to the browser.
///
/// The token is checked against Google's own public key
/// set (the same JWKS endpoint Google's libraries use),
/// so a token forged on a server that cannot reach
/// Google, or signed with a different client's key, is
/// rejected here rather than trusted on its word.
///
/// The key set is cached briefly: Google rotates it, and
/// every token carries a "kid" that selects the right
/// key, so a cached set keeps working across a rotation.
/// </summary>
public static class GoogleTokenValidator
{
    private static readonly HttpClient Http = new();

    private static (DateTimeOffset fetchedAt, Dictionary<string, RsaSecurityKey> keys) _cache =
        (DateTimeOffset.MinValue, new Dictionary<string, RsaSecurityKey>());

    private static readonly object Gate = new();

    public static async Task<GoogleTokenPayload> ValidateAsync(
        string idToken,
        string clientId)
    {
        var handler = new JwtSecurityTokenHandler();

        if (!handler.CanReadToken(idToken))
        {
            throw new SecurityTokenException("Not a JWT.");
        }

        var token = handler.ReadJwtToken(idToken);

        var keys = await GetKeysAsync();

        var kid = token.Header.Kid;

        if (string.IsNullOrEmpty(kid) || !keys.TryGetValue(kid, out var key))
        {
            throw new SecurityTokenException("Unknown signing key.");
        }

        var parameters = new TokenValidationParameters
        {
            IssuerSigningKey = key,
            ValidIssuers = new[]
            {
                "accounts.google.com",
                "https://accounts.google.com"
            },
            ValidAudience = clientId,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        // Throws on any failure: bad signature, wrong
        // audience, expired, wrong issuer.
        handler.ValidateToken(idToken, parameters, out _);

        var payload = token.Payload;

        return new GoogleTokenPayload
        {
            Subject = payload.Sub ?? "",
            Email = payload["email"] as string,
            Name = payload["given_name"] as string,
            FamilyName = payload["family_name"] as string
        };
    }

    private static async Task<Dictionary<string, RsaSecurityKey>> GetKeysAsync()
    {
        lock (Gate)
        {
            if (_cache.keys.Count > 0 &&
                (DateTimeOffset.UtcNow - _cache.fetchedAt).TotalMinutes < 60)
            {
                return _cache.keys;
            }
        }

        var json = await Http.GetStringAsync(
            "https://www.googleapis.com/oauth2/v3/certs");

        var keys = new Dictionary<string, RsaSecurityKey>();

        using (var document = JsonDocument.Parse(json))
        {
            foreach (var element in document.RootElement
                .GetProperty("keys")
                .EnumerateArray())
            {
                if (!element.TryGetProperty("kty", out var kty) ||
                    kty.GetString() != "RSA" ||
                    !element.TryGetProperty("n", out var modulus) ||
                    !element.TryGetProperty("e", out var exponent) ||
                    !element.TryGetProperty("kid", out var kidElement))
                {
                    continue;
                }

                var rsa = RSA.Create();

                rsa.ImportParameters(new RSAParameters
                {
                    Modulus = Base64UrlEncoder.DecodeBytes(modulus.GetString()!),
                    Exponent = Base64UrlEncoder.DecodeBytes(exponent.GetString()!)
                });

                keys[kidElement.GetString()!] = new RsaSecurityKey(rsa);
            }
        }

        lock (Gate)
        {
            _cache = (DateTimeOffset.UtcNow, keys);
        }

        return keys;
    }
}

public class GoogleTokenPayload
{
    public string Subject { get; set; } = "";

    public string? Email { get; set; }

    public string? Name { get; set; }

    public string? FamilyName { get; set; }
}
