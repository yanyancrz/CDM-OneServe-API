using System.Buffers.Text;
using System.Text;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Notifications;
using CDM_OneServe_API.Models.Notifications;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Notifications;

/// <summary>
/// Owns the browser subscription store: register, remove, list, validate,
/// and expire the rows a push service no longer recognises.
/// </summary>
public class PushSubscriptionService
{
    /// <summary>FCM / autopush / WNS endpoints comfortably fit in 500 chars.</summary>
    public const int MaxEndpointLength = 500;

    /// <summary>An unsubscribed browser is hard-deleted after this long.</summary>
    public static readonly TimeSpan ExpiredRetention = TimeSpan.FromDays(90);

    private readonly AppDbContext _db;

    public PushSubscriptionService(AppDbContext db) => _db = db;

    // =========================================================
    // READ
    // =========================================================

    public async Task<List<PushSubscriptionStatusDto>> ListAsync(int userId, CancellationToken ct = default) =>
        await _db.PushSubscriptions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.IsActive)
            .ThenByDescending(s => s.CreatedAt)
            .Select(s => new PushSubscriptionStatusDto
            {
                PushSubscriptionId = s.PushSubscriptionId,
                Endpoint = s.Endpoint,
                DeviceLabel = s.DeviceLabel,
                IsActive = s.IsActive,
                CreatedAt = s.CreatedAt,
                LastUsedAt = s.LastUsedAt
            })
            .ToListAsync(ct);

    public async Task<bool> HasActiveAsync(int userId, CancellationToken ct = default) =>
        await _db.PushSubscriptions.AnyAsync(s => s.UserId == userId && s.IsActive, ct);

    // =========================================================
    // REGISTER / REMOVE
    // =========================================================

    /// <summary>
    /// Stores what <c>PushManager.subscribe()</c> produced.
    ///
    /// Ownership comes from the JWT, never from the body, so a stolen
    /// endpoint can never be re-pointed at somebody else's account: the
    /// endpoint is globally unique, and whichever user re-subscribes
    /// becomes its owner (that is exactly what a browser does when the
    /// same person signs in again, and it is why the endpoint is the key).
    /// </summary>
    public async Task<(bool Success, string Message, PushSubscriptionStatusDto? Subscription)>
        SubscribeAsync(int userId, PushSubscriptionDto dto, string? userAgent, CancellationToken ct = default)
    {
        var problem = Validate(dto);
        if (problem is not null) return (false, problem, null);

        var endpoint = dto.Endpoint.Trim();
        var p256dh = dto.P256dh.Trim();
        var auth = dto.Auth.Trim();

        var existing = await _db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);

        var label = SoftTrim(dto.DeviceLabel, 100) ?? GuessDeviceLabel(userAgent);

        if (existing is null)
        {
            existing = new PushSubscription
            {
                UserId = userId,
                Endpoint = endpoint,
                P256dh = p256dh,
                Auth = auth,
                DeviceLabel = label,
                UserAgent = SoftTrim(userAgent, 500),
                IsActive = true,
                DeactivatedAt = null,
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow
            };

            _db.PushSubscriptions.Add(existing);
        }
        else
        {
            // Re-subscribing is the browser telling us the keys rotated,
            // or that an expired row is live again: refresh everything.
            existing.UserId = userId;
            existing.P256dh = p256dh;
            existing.Auth = auth;
            existing.DeviceLabel = label;
            existing.UserAgent = SoftTrim(userAgent, 500);
            existing.IsActive = true;
            existing.DeactivatedAt = null;
            existing.LastUsedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        return (
            true,
            "Notifications are enabled for this device.",
            new PushSubscriptionStatusDto
            {
                PushSubscriptionId = existing.PushSubscriptionId,
                Endpoint = existing.Endpoint,
                DeviceLabel = existing.DeviceLabel,
                IsActive = existing.IsActive,
                CreatedAt = existing.CreatedAt,
                LastUsedAt = existing.LastUsedAt
            });
    }

    /// <summary>
    /// Removes a subscription. Touching somebody else's row is a 404, not
    /// a 403, so the response cannot be used to probe which ids exist.
    /// </summary>
    public async Task<(bool Success, string Message, bool NotFound)>
        UnsubscribeAsync(int userId, PushUnsubscribeDto dto, CancellationToken ct = default)
    {
        IQueryable<PushSubscription> query = _db.PushSubscriptions;

        if (dto.PushSubscriptionId is int id)
        {
            query = query.Where(s => s.PushSubscriptionId == id);
        }
        else if (!string.IsNullOrWhiteSpace(dto.Endpoint))
        {
            var endpoint = dto.Endpoint.Trim();
            query = query.Where(s => s.Endpoint == endpoint);
        }
        else
        {
            return (false, "Send either the endpoint or the subscription id.", false);
        }

        var rows = await query.Where(s => s.UserId == userId).ToListAsync(ct);

        if (rows.Count == 0)
            return (false, "Subscription not found.", true);

        _db.PushSubscriptions.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);

        return (true, "Notifications are off for this device.", false);
    }

    // =========================================================
    // VALIDATION
    // =========================================================

    /// <summary>
    /// Sanity-checks a subscription straight from the browser. Everything
    /// that reaches the Node service has already passed through here, so
    /// the sender can trust the shape of what it encrypts.
    ///
    /// Every message names the field and what it expected, so a 400 tells the
    /// caller how to fix the request instead of just "not valid".
    /// </summary>
    public static string? Validate(PushSubscriptionDto dto)
    {
        var endpoint = (dto.Endpoint ?? string.Empty).Trim();

        if (endpoint.Length == 0)
            return "The push endpoint is missing.";

        if (endpoint.Length > MaxEndpointLength)
            return $"The push endpoint is too long (max {MaxEndpointLength} characters).";

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            return "The push endpoint must be an absolute https URL.";
        }

        return CheckKey("encryption key (p256dh)", dto.P256dh, P256dhLength)
            ?? CheckKey("authentication secret (auth)", dto.Auth, AuthLength);
    }

    /// <summary>Uncompressed P-256 point: 0x04 || X(32) || Y(32).</summary>
    public const int P256dhLength = 65;

    /// <summary>The browser's 128-bit authentication secret.</summary>
    public const int AuthLength = 16;

    /// <summary>Longest key worth inspecting; anything past this is junk.</summary>
    private const int MaxKeyLength = 200;

    private static string? CheckKey(string label, string? value, int expectedBytes)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return
                $"The subscription {label} is missing. Send it as an unpadded base64url string " +
                "(JSON.stringify of a raw PushSubscription turns the keys into empty objects - " +
                "send subscription.keys.p256dh as a string).";
        }

        if (text.Length > MaxKeyLength)
            return $"The subscription {label} is too long ({text.Length} characters).";

        var bad = text.FirstOrDefault(ch =>
            !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_'));

        if (bad != default)
        {
            return
                $"The subscription {label} contains '{bad}', which is not a base64url character. " +
                "Use unpadded base64url: no '+', '/' or '='.";
        }

        Span<byte> bytes = stackalloc byte[text.Length];

        if (!Base64Url.TryDecodeFromChars(text, bytes, out var written))
            return $"The subscription {label} is not valid base64url.";

        if (written != expectedBytes)
        {
            return
                $"The subscription {label} decoded to {written} bytes, expected {expectedBytes}.";
        }

        return null;
    }

    // =========================================================
    // EXPIRED SUBSCRIPTION CLEANUP
    // =========================================================

    /// <summary>
    /// Called after the Node service reports a 404/410: the push service
    /// no longer knows this subscription, so we stop counting it as live
    /// (which keeps the dispatcher from queueing undeliverable work) but
    /// keep the row long enough for the user to see and fix it.
    /// </summary>
    public async Task DeactivateAsync(int pushSubscriptionId, CancellationToken ct = default)
    {
        var subscription = await _db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.PushSubscriptionId == pushSubscriptionId, ct);

        if (subscription is null || !subscription.IsActive) return;

        subscription.IsActive = false;
        subscription.DeactivatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkDeliveredAsync(int pushSubscriptionId, CancellationToken ct = default)
    {
        await _db.PushSubscriptions
            .Where(s => s.PushSubscriptionId == pushSubscriptionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.LastUsedAt, DateTime.UtcNow)
                .SetProperty(s => s.IsActive, true), ct);
    }

    /// <summary>Hard-deletes subscriptions that have been dead for ages.</summary>
    public async Task<int> DeleteExpiredAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - ExpiredRetention;

        var dead = await _db.PushSubscriptions
            .Where(s => !s.IsActive && (s.DeactivatedAt == null || s.DeactivatedAt < cutoff))
            .ToListAsync(ct);

        if (dead.Count == 0) return 0;

        _db.PushSubscriptions.RemoveRange(dead);
        await _db.SaveChangesAsync(ct);

        return dead.Count;
    }

    private static string? SoftTrim(string? value, int max)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return null;
        return text.Length <= max ? text : text[..max];
    }

    private static string? GuessDeviceLabel(string? userAgent)
    {
        var ua = userAgent ?? string.Empty;

        if (ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase)) return "Edge";
        if (ua.Contains("OPR/", StringComparison.OrdinalIgnoreCase)) return "Opera";
        if (ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) &&
            !ua.Contains("SamsungBrowser", StringComparison.OrdinalIgnoreCase)) return "Chrome";
        if (ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase)) return "Safari";

        return null;
    }
}
