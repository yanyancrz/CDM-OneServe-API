using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Platform;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Platform;

/// <summary>
/// Append-only security event log plus the queries the Security Center uses.
///
/// Events are recorded by the auth and session routes (successful and failed
/// sign-ins, resets, revocations). The center only READS this table; nothing
/// in the authentication path depends on it, so a logging failure can never
/// block a legitimate sign-in.
/// </summary>
public class SecurityEventService
{
    private readonly AppDbContext _db;
    private readonly ILogger<SecurityEventService> _logger;

    public SecurityEventService(
        AppDbContext db,
        ILogger<SecurityEventService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ---------------------------------------------------------
    // WRITE
    // ---------------------------------------------------------

    public async Task RecordAsync(
        string eventType,
        string severity,
        int? userId = null,
        string? email = null,
        string? idNumber = null,
        string? details = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? deviceLabel = null,
        CancellationToken ct = default)
    {
        try
        {
            _db.SecurityEvents.Add(new SecurityEvent
            {
                UserId = userId,
                Email = Truncate(email, 255),
                IdNumber = Truncate(idNumber, 50),
                EventType = eventType,
                Severity = severity,
                Details = Truncate(details, 500),
                IpAddress = Truncate(ipAddress, 64),
                UserAgent = Truncate(userAgent, 500),
                DeviceLabel = Truncate(deviceLabel, 120),
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Never break the request that triggered the event.
            _logger.LogWarning(ex, "Could not record security event {EventType}", eventType);
        }
    }

    public async Task RecordFromRequestAsync(
        HttpContext? http,
        string eventType,
        string severity,
        int? userId = null,
        string? email = null,
        string? idNumber = null,
        string? details = null,
        CancellationToken ct = default)
    {
        var forwarded = http?.Request.Headers["X-Forwarded-For"].ToString();

        var ip = !string.IsNullOrWhiteSpace(forwarded)
            ? forwarded.Split(',')[0].Trim()
            : http?.Connection.RemoteIpAddress?.ToString();

        var agent = http?.Request.Headers.UserAgent.ToString();

        await RecordAsync(
            eventType,
            severity,
            userId,
            email,
            idNumber,
            details,
            ip,
            agent,
            string.IsNullOrWhiteSpace(agent) ? null : DeviceLabel.FromUserAgent(agent),
            ct);
    }

    /// <summary>
    /// Called after a successful password sign-in. Flags a device the account
    /// has not used in the last 30 days, when the setting is on.
    /// </summary>
    public async Task FlagNewDeviceAsync(
        int userId,
        string? email,
        string? deviceLabel,
        HttpContext? http,
        string? ipAddress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceLabel)) return;

        var since = DateTime.UtcNow.AddDays(-30);

        var seen = await _db.UserSessions
            .AsNoTracking()
            .AnyAsync(s =>
                s.UserId == userId &&
                s.DeviceLabel == deviceLabel &&
                s.CreatedAt >= since, ct);

        if (seen) return;

        await RecordAsync(
            "new_device_login",
            SecuritySeverities.Warning,
            userId,
            email,
            null,
            $"First sign-in from this device in 30 days: {deviceLabel}",
            ipAddress,
            http?.Request.Headers.UserAgent.ToString(),
            deviceLabel,
            ct);
    }

    // ---------------------------------------------------------
    // READ
    // ---------------------------------------------------------

    public async Task<List<SecurityEvent>> QueryEventsAsync(
        string? eventType,
        string? severity,
        int? userId,
        string? search,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var rows = _db.SecurityEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(eventType))
            rows = rows.Where(x => x.EventType == eventType);

        if (!string.IsNullOrWhiteSpace(severity))
            rows = rows.Where(x => x.Severity == severity);

        if (userId is { } id)
            rows = rows.Where(x => x.UserId == id);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            rows = rows.Where(x =>
                (x.Email != null && x.Email.Contains(term)) ||
                (x.IpAddress != null && x.IpAddress.Contains(term)) ||
                (x.DeviceLabel != null && x.DeviceLabel.Contains(term)) ||
                (x.Details != null && x.Details.Contains(term)));
        }

        if (from is { } start) rows = rows.Where(x => x.CreatedAt >= start);
        if (to is { } end) rows = rows.Where(x => x.CreatedAt <= end);

        return await rows
            .OrderByDescending(x => x.CreatedAt)
            .Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 10, 100))
            .Take(Math.Clamp(pageSize, 10, 100))
            .ToListAsync(ct);
    }

    public async Task<int> CountEventsAsync(
        string? eventType,
        DateTime from,
        CancellationToken ct = default)
    {
        var rows = _db.SecurityEvents.AsNoTracking()
            .Where(x => x.CreatedAt >= from);

        if (!string.IsNullOrWhiteSpace(eventType))
            rows = rows.Where(x => x.EventType == eventType);

        return await rows.CountAsync(ct);
    }

    /// <summary>
    /// Accounts currently locked by the optional lockout policy. Purely a
    /// read: the login route owns the locking itself.
    /// </summary>
    public async Task<List<LockedAccount>> GetLockedAccountsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        return await _db.Users
            .AsNoTracking()
            .Where(u => u.LockoutUntil != null && u.LockoutUntil > now)
            .OrderByDescending(u => u.LockoutUntil)
            .Select(u => new LockedAccount(
                u.Id,
                u.FullName,
                u.Email,
                u.Role,
                u.FailedLoginCount,
                u.LockoutUntil!.Value))
            .ToListAsync(ct);
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = value.Trim();

        return text.Length <= max ? text : text[..max];
    }
}

public sealed record LockedAccount(
    int UserId,
    string FullName,
    string Email,
    string Role,
    int FailedLoginCount,
    DateTime LockoutUntil);
