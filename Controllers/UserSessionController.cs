using System.Security.Claims;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers;

/// <summary>
/// "Is my account signed in somewhere else?"
///
/// Reads the <c>X-Session-Key</c> header to know which device is asking, then
/// reports any session for the same user created AFTER it. A newer session from
/// a different key means somebody else signed in on this account since.
///
/// Deliberately NOT part of authentication. Revoking a session here does not
/// invalidate that device's JWT - it only clears the warning, which is what the
/// user asked for. Real token revocation is not possible with a stateless JWT.
/// </summary>
[ApiController]
[Route("api/auth/session")]
[Authorize]
public class UserSessionController : ControllerBase
{
    /// <summary>How far back to look for "another device".</summary>
    private static readonly TimeSpan Lookback = TimeSpan.FromDays(90);

    private readonly AppDbContext _context;

    public UserSessionController(AppDbContext context) => _context = context;

    /// <summary>
    /// Whether this account has been signed in somewhere else since this
    /// device did.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return UnauthorizedSession();

        var key = Request.Headers["X-Session-Key"].ToString().Trim();

        // Nothing to compare against: the browser signed in before this feature
        // existed, or cleared its storage. Report "unknown" rather than guessing,
        // so no false "another device" warning is ever shown.
        if (string.IsNullOrEmpty(key))
        {
            return Ok(new
            {
                success = true,
                currentSessionKnown = false,
                hasOtherDevice = false
            });
        }

        var mine = await _context.UserSessions.AsNoTracking()
            .Where(s => s.SessionKey == key && s.UserId == userId && !s.IsRevoked)
            .Select(s => new { s.CreatedAt })
            .FirstOrDefaultAsync(ct);

        if (mine is null)
        {
            // Someone answered "sign out everywhere" from this device, or the
            // row was pruned. Either way, stop warning.
            return Ok(new
            {
                success = true,
                currentSessionKnown = true,
                hasOtherDevice = false,
                currentSessionRevoked = true
            });
        }

        var since = DateTime.Now - Lookback;

        // Anything newer than THIS device, from a different key.
        var others = await _context.UserSessions.AsNoTracking()
            .Where(s => s.UserId == userId &&
                        s.SessionKey != key &&
                        !s.IsRevoked &&
                        s.CreatedAt > mine.CreatedAt &&
                        s.CreatedAt >= since)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        var latest = others.FirstOrDefault();

        // Housekeeping: this device is alive.
        await _context.UserSessions
            .Where(s => s.SessionKey == key)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.LastSeenAt, DateTime.Now), ct);

        return Ok(new
        {
            success = true,
            currentSessionKnown = true,
            hasOtherDevice = others.Count > 0,
            otherDeviceCount = others.Count,
            otherDeviceLabel = latest?.DeviceLabel ?? "another device",
            otherDeviceAt = latest?.CreatedAt,
            // Where the user should land to act on it.
            recommendations = others.Count > 0
                ? new[] { "changePassword", "signOutOthers" }
                : Array.Empty<string>()
        });
    }

    /// <summary>
    /// Stops the "another device" warning by marking every OTHER session of
    /// this account revoked. It does NOT sign those devices out - that is
    /// impossible with a stateless token - it records that the user has seen
    /// and dismissed the warning.
    /// </summary>
    [HttpPost("dismiss-others")]
    public async Task<IActionResult> DismissOthers(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return UnauthorizedSession();

        var key = Request.Headers["X-Session-Key"].ToString().Trim();
        if (string.IsNullOrEmpty(key))
            return BadRequest(new { success = false, message = "This device has no session key." });

        var revoked = await _context.UserSessions
            .Where(s => s.UserId == userId && s.SessionKey != key && !s.IsRevoked)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.IsRevoked, true), ct);

        return Ok(new
        {
            success = true,
            message = revoked > 0
                ? "Warning cleared. If this was not you, change your password."
                : "Nothing to clear.",
            revokedCount = revoked
        });
    }

    /// <summary>
    /// The real "log out on all devices".
    ///
    /// Stamps Users.TokensValidFrom with "now", which makes every token
    /// minted before this moment fail validation on the next request - this
    /// device included, so the user lands back on the login screen. Unlike
    /// <see cref="DismissOthers"/> this actually ends the sessions, because
    /// the token's own issued-at (iat) claim can no longer satisfy the check
    /// in Program.cs.
    /// </summary>
    [HttpPost("everywhere")]
    public async Task<IActionResult> SignOutEverywhere(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return UnauthorizedSession();

        // UtcNow, not Now: this is compared against the token's iat claim,
        // which is UTC. The value is stored as the raw UTC wall clock and
        // read back as UTC in Program.cs, so the two never drift.
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

        var stamped = await _context.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.TokensValidFrom, now), ct);

        if (stamped == 0)
        {
            return NotFound(new
            {
                success = false,
                message = "Account not found."
            });
        }

        // Keep the device list honest: nothing is signed in any more, so the
        // "another device" warning must not survive this.
        await _context.UserSessions
            .Where(s => s.UserId == userId && !s.IsRevoked)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.IsRevoked, true), ct);

        return Ok(new
        {
            success = true,
            message = "Signed out on all devices.",
            signedOutAt = now
        });
    }

    /// <summary>
    /// The account's recent logins, newest first: device, network address
    /// and times. Powers the dashboard's "Recent Logins" and the Profile
    /// login-history screen.
    ///
    /// The network address is the raw IP the login came from (or the first
    /// proxy hop) - it says which network, not a GPS pin. Null when the
    /// proxy never exposed one.
    /// </summary>
    [HttpGet("logins")]
    public async Task<IActionResult> Logins(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return UnauthorizedSession();

        var key = Request.Headers["X-Session-Key"].ToString().Trim();

        var since = DateTime.Now - Lookback;

        var rows = await _context.UserSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.CreatedAt >= since)
            .OrderByDescending(s => s.CreatedAt)
            .Take(15)
            .Select(s => new
            {
                device = s.DeviceLabel ?? "Unknown device",
                ipAddress = s.IpAddress,
                loggedInAt = s.CreatedAt,
                lastSeenAt = s.LastSeenAt,
                isRevoked = s.IsRevoked,
                isCurrent = key.Length > 0 && s.SessionKey == key
            })
            .ToListAsync(ct);

        return Ok(new { success = true, logins = rows });
    }

    /// <summary>
    /// Removes every session of this account INCLUDING this device, so the
    /// warning never comes back for a device the user deliberately abandoned.
    /// </summary>
    [HttpPost("clear-mine")]
    public async Task<IActionResult> ClearMine(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return UnauthorizedSession();

        var key = Request.Headers["X-Session-Key"].ToString().Trim();
        if (string.IsNullOrEmpty(key)) return Ok(new { success = true });

        await _context.UserSessions
            .Where(s => s.UserId == userId && s.SessionKey == key)
            .ExecuteDeleteAsync(ct);

        return Ok(new { success = true, message = "This device's session record was cleared." });
    }

    private IActionResult UnauthorizedSession() => Unauthorized(new
    {
        success = false,
        message = "Invalid session. Please log in again."
    });

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;
}
