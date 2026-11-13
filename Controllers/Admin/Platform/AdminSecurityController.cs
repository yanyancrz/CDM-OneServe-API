using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Platform;
using CDM_OneServe_API.Services.Admin.Platform;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers.Admin.Platform;

/// <summary>
/// Security Center (Part 10).
///
/// Reads the existing <c>user_sessions</c> table and the new security event
/// log. Signing a device out uses the SAME mechanism as the user-facing
/// "log out everywhere": revoking sessions and stamping
/// <c>TokensValidFrom</c>, which is what actually kills the JWTs. There is
/// no second session system.
/// </summary>
[Route("api/admin/security")]
public class AdminSecurityController : AdminPlatformControllerBase
{
    private const string Module = "Security";
    private const string Category = "Security";

    private readonly AppDbContext _db;

    public AdminSecurityController(
        AppDbContext db,
        PermissionService permissions,
        AdminAuditService audit,
        SecurityEventService security)
        : base(permissions, audit, security)
    {
        _db = db;
    }

    // GET: api/admin/security
    [HttpGet]
    public async Task<IActionResult> GetOverview(CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.SecurityView, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var now = DateTime.UtcNow;
        var day = now.AddHours(-24);
        var week = now.AddDays(-7);

        var failed24h = await Security.CountEventsAsync(
            SecurityEventTypes.LoginFailed, day, ct);

        var succeeded24h = await Security.CountEventsAsync(
            SecurityEventTypes.LoginSucceeded, day, ct);

        var resets7d = await Security.CountEventsAsync(
            SecurityEventTypes.PasswordResetCompleted, week, ct);

        var lockedOut7d = await Security.CountEventsAsync(
            SecurityEventTypes.LockedOut, week, ct);

        var denied7d = await Security.CountEventsAsync(
            SecurityEventTypes.PrivilegedAccessDenied, week, ct);

        var newDevices7d = await Security.CountEventsAsync(
            "new_device_login", week, ct);

        var activeSessions = await _db.UserSessions
            .AsNoTracking()
            .CountAsync(s =>
                !s.IsRevoked &&
                s.LoggedOutAt == null &&
                (s.ExpiresAt == null || s.ExpiresAt > now), ct);

        var lockedAccounts = await Security.GetLockedAccountsAsync(ct);

        var suspended = await _db.Users
            .AsNoTracking()
            .CountAsync(u => u.AccountStatus == "Suspended", ct);

        var recentAlerts = await _db.SecurityEvents
            .AsNoTracking()
            .Where(x => x.Severity != SecuritySeverities.Info &&
                        x.CreatedAt >= week)
            .OrderByDescending(x => x.CreatedAt)
            .Take(15)
            .ToListAsync(ct);

        // A failed-login spike is worth surfacing on its own.
        var alerts = new List<object>();

        if (failed24h >= 20)
        {
            alerts.Add(new
            {
                severity = SecuritySeverities.Warning,
                title = "Elevated failed sign-ins",
                message = $"{failed24h} failed sign-ins in the last 24 hours."
            });
        }

        if (newDevices7d > 0)
        {
            alerts.Add(new
            {
                severity = SecuritySeverities.Info,
                title = "New device sign-ins",
                message = $"{newDevices7d} sign-in(s) from a device not seen in 30 days."
            });
        }

        if (denied7d > 0)
        {
            alerts.Add(new
            {
                severity = SecuritySeverities.Warning,
                title = "Denied privileged actions",
                message = $"{denied7d} attempt(s) were denied by the permission check."
            });
        }

        if (lockedAccounts.Count > 0)
        {
            alerts.Add(new
            {
                severity = SecuritySeverities.Warning,
                title = "Locked accounts",
                message = $"{lockedAccounts.Count} account(s) are currently locked."
            });
        }

        return Ok(new
        {
            generatedAt = now,
            loginAttempts24h = new { succeeded = succeeded24h, failed = failed24h },
            passwordResets7d = resets7d,
            lockouts7d = lockedOut7d,
            deniedPrivilegedActions7d = denied7d,
            newDeviceLogins7d = newDevices7d,
            activeSessions,
            suspendedAccounts = suspended,
            lockedAccounts,
            alerts,
            recentAlerts
        });
    }

    // GET: api/admin/security/events
    [HttpGet("events")]
    public async Task<IActionResult> GetEvents(
        [FromQuery] string? type,
        [FromQuery] string? severity,
        [FromQuery] int? userId,
        [FromQuery] string? search,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.SecurityView, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var items = await Security.QueryEventsAsync(
            type, severity, userId, search, from, to, page, pageSize, ct);

        return Ok(new
        {
            items,
            page,
            pageSize,
            hasMore = items.Count == Math.Clamp(pageSize, 10, 100)
        });
    }

    // GET: api/admin/security/sessions
    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions(
        [FromQuery] bool activeOnly = true,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.SecurityView, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var now = DateTime.UtcNow;
        var size = Math.Clamp(pageSize, 10, 100);

        // Explicit join: UserSession deliberately has no navigation property,
        // so adding the Security Center did not change the auth model or add a
        // foreign key that could reject existing rows.
        var query =
            from s in _db.UserSessions.AsNoTracking()
            join u in _db.Users.AsNoTracking() on s.UserId equals u.Id into gj
            from u in gj.DefaultIfEmpty()
            select new
            {
                sessionId = s.UserSessionId,
                userId = s.UserId,
                fullName = u != null ? u.FullName : null,
                email = u != null ? u.Email : null,
                idNumber = u != null ? u.IdNumber : null,
                role = u != null ? u.Role : null,
                s.DeviceLabel,
                s.IpAddress,
                s.CreatedAt,
                s.LastSeenAt,
                s.ExpiresAt,
                s.IsRevoked,
                s.LoggedOutAt,
                isActive = !s.IsRevoked &&
                           s.LoggedOutAt == null &&
                           (s.ExpiresAt == null || s.ExpiresAt > now)
            };

        if (activeOnly)
        {
            query = query.Where(x => x.isActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            query = query.Where(x =>
                (x.fullName != null && x.fullName.Contains(term)) ||
                (x.email != null && x.email.Contains(term)) ||
                (x.idNumber != null && x.idNumber.Contains(term)) ||
                (x.DeviceLabel != null && x.DeviceLabel.Contains(term)) ||
                (x.IpAddress != null && x.IpAddress.Contains(term)));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.LastSeenAt)
            .Skip((Math.Max(1, page) - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return Ok(new
        {
            items,
            page,
            pageSize = size,
            totalItems = total,
            totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)size)
        });
    }

    // GET: api/admin/security/users/{userId}/login-history
    [HttpGet("users/{userId:int}/login-history")]
    public async Task<IActionResult> GetLoginHistory(
        int userId,
        [FromQuery] int take = 20,
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.SecurityView, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var size = Math.Clamp(take, 5, 100);

        var logins = await _db.UserActivities
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.Action == UserActivityActions.Login)
            .OrderByDescending(a => a.CreatedAt)
            .Take(size)
            .Select(a => new { a.CreatedAt, a.Description })
            .ToListAsync(ct);

        var sessions = await _db.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(size)
            .Select(s => new
            {
                s.UserSessionId,
                s.DeviceLabel,
                s.IpAddress,
                s.CreatedAt,
                s.LastSeenAt,
                s.ExpiresAt,
                s.IsRevoked,
                s.LoggedOutAt,
                s.LogoutReason
            })
            .ToListAsync(ct);

        var events = await Security.QueryEventsAsync(
            null, null, userId, null, null, null, 1, size, ct);

        return Ok(new { userId, logins, sessions, events });
    }

    // POST: api/admin/security/sessions/{sessionId:int}/revoke
    [HttpPost("sessions/{sessionId:int}/revoke")]
    public async Task<IActionResult> RevokeSession(
        int sessionId,
        [FromQuery] string? reason,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.SecurityManage, PermissionLevels.Manage, ct);

        if (auth.Failure is not null) return auth.Failure;

        var session = await _db.UserSessions
            .FirstOrDefaultAsync(s => s.UserSessionId == sessionId, ct);

        if (session is null)
        {
            return NotFound(new { message = "Session not found." });
        }

        if (session.IsRevoked)
        {
            return BadRequest(new { message = "That session is already revoked." });
        }

        var owner = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == session.UserId)
            .Select(u => new { u.Email, u.IdNumber, u.FullName })
            .FirstOrDefaultAsync(ct);

        session.IsRevoked = true;
        session.LoggedOutAt = DateTime.UtcNow;
        session.LogoutReason = string.IsNullOrWhiteSpace(reason)
            ? "Revoked by an administrator"
            : Trunc(reason, 200);

        await _db.SaveChangesAsync(ct);

        await Security.RecordFromRequestAsync(
            HttpContext,
            SecurityEventTypes.SessionRevoked,
            SecuritySeverities.Warning,
            session.UserId,
            owner?.Email,
            owner?.IdNumber,
            $"Session on {session.DeviceLabel ?? "unknown device"} revoked by an administrator.",
            ct);

        await WriteAuditAsync(
            Module, Category,
            "Revoked a device session",
            entityType: "UserSession",
            entityId: sessionId,
            details: owner is null
                ? null
                : $"Account: {owner.FullName} ({owner.Email})",
            reason: reason,
            ct: ct);

        return Ok(new
        {
            message = "The session was signed out. The device's token stays valid until it expires; use 'Sign out all devices' to end it immediately."
        });
    }

    // POST: api/admin/security/users/{userId:int}/sign-out-all
    [HttpPost("users/{userId:int}/sign-out-all")]
    public async Task<IActionResult> SignOutAllDevices(
        int userId,
        [FromQuery] string? reason,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.SecurityManage, PermissionLevels.Manage, ct);

        if (auth.Failure is not null) return auth.Failure;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }

        var now = DateTime.UtcNow;

        // Same mechanism as the user-facing route: tokens minted before this
        // stamp fail OnTokenValidated, so every device is really signed out.
        user.TokensValidFrom = now;

        var sessions = await _db.UserSessions
            .Where(s => s.UserId == userId && !s.IsRevoked && s.LoggedOutAt == null)
            .ToListAsync(ct);

        foreach (var session in sessions)
        {
            session.IsRevoked = true;
            session.LoggedOutAt = now;
            session.LogoutReason = "Signed out by an administrator";
        }

        await _db.SaveChangesAsync(ct);

        await Security.RecordFromRequestAsync(
            HttpContext,
            SecurityEventTypes.SignedOutEverywhere,
            SecuritySeverities.Warning,
            userId,
            user.Email,
            user.IdNumber,
            $"{sessions.Count} session(s) signed out by an administrator.",
            ct);

        await WriteAuditAsync(
            Module, Category,
            "Signed an account out of all devices",
            entityType: "User",
            entityId: userId,
            details: $"{sessions.Count} session(s) closed. Account: {user.FullName} ({user.Email}).",
            reason: reason,
            ct: ct);

        return Ok(new
        {
            message = $"Signed out {sessions.Count} session(s).",
            sessionsClosed = sessions.Count
        });
    }
}
