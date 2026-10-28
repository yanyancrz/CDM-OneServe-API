using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.LostFound;
using CDM_OneServe_API.Models;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.LostFound;

/// <summary>
/// Administrator management, login monitoring and the append-only activity
/// log for the Lost &amp; Found console.
///
/// Every mutating method re-checks the caller's role in the database. The
/// controller's [Authorize] attribute is the first lock; this is the second,
/// so a role change taking effect mid-session can never leave a stale token
/// able to create administrators.
/// </summary>
public class LostFoundAdminService
{
    private const string Module = "Lost & Found";

    private readonly AppDbContext _db;
    private readonly LostFoundAuditService _audit;

    public LostFoundAdminService(AppDbContext db, LostFoundAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    // =========================================================
    // WHO AM I
    // =========================================================

    public async Task<LostFoundResult<LostFoundAdminMeDto>> GetMeAsync(
        int userId, CancellationToken ct = default)
    {
        var actor = await _audit.GetActorAsync(userId, ct);
        if (actor is null)
            return LostFoundResult<LostFoundAdminMeDto>.NotFound("Account not found.");

        if (!actor.IsAdmin)
            return LostFoundResult<LostFoundAdminMeDto>.Forbidden(
                "Lost & Found administrator access required.");

        var mustChange = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.MustChangePassword)
            .FirstOrDefaultAsync(ct);

        return LostFoundResult<LostFoundAdminMeDto>.Ok(new LostFoundAdminMeDto
        {
            Id = actor.Id,
            FullName = actor.FullName,
            Email = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(ct) ?? string.Empty,
            Role = actor.Role,
            RoleLabel = actor.RoleLabel,
            IsSuperAdmin = actor.CanManageAdmins,
            CanManageAdmins = actor.CanManageAdmins,
            MustChangePassword = mustChange,
        });
    }

    // =========================================================
    // ADMIN MANAGEMENT (SUPER ADMIN ONLY)
    // =========================================================

    public async Task<LostFoundResult<List<SubAdminDto>>> GetAdminsAsync(
        int actorUserId, CancellationToken ct = default)
    {
        if (!await CanManageAsync(actorUserId, ct))
            return LostFoundResult<List<SubAdminDto>>.Forbidden(
                "Only a Super Admin can manage administrator accounts.");

        var admins = await _db.Users
            .AsNoTracking()
            .Where(u => u.Role == LostFoundRoles.SubAdmin ||
                        u.Role == LostFoundRoles.SuperAdmin ||
                        u.Role == LostFoundRoles.LegacyAdmin)
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);

        var ids = admins.Select(a => a.Id).ToList();

        var lastLogins = await _db.UserSessions
            .AsNoTracking()
            .Where(s => ids.Contains(s.UserId) && !s.IsRevoked)
            .GroupBy(s => s.UserId)
            .Select(g => new { UserId = g.Key, LastLoginAt = g.Max(s => s.CreatedAt) })
            .ToDictionaryAsync(x => x.UserId, x => (DateTime?)x.LastLoginAt, ct);

        return LostFoundResult<List<SubAdminDto>>.Ok(admins.Select(u => new SubAdminDto
        {
            Id = u.Id,
            FullName = u.FullName,
            Email = u.Email,
            Role = u.Role,
            RoleLabel = LostFoundRoles.Label(u.Role),
            IsActive = IsActive(u.AccountStatus),
            MustChangePassword = u.MustChangePassword,
            CreatedAt = u.CreatedAt,
            LastLoginAt = lastLogins.TryGetValue(u.Id, out var last) ? last : null,
        }).ToList());
    }

    public async Task<LostFoundResult<SubAdminDto>> CreateSubAdminAsync(
        int actorUserId, CreateSubAdminDto dto, CancellationToken ct = default)
    {
        if (!await CanManageAsync(actorUserId, ct))
            return LostFoundResult<SubAdminDto>.Forbidden(
                "Only a Super Admin can create administrator accounts.");

        var fullName = (dto.FullName ?? string.Empty).Trim();
        var email = (dto.Email ?? string.Empty).Trim();
        var password = dto.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(fullName))
            return LostFoundResult<SubAdminDto>.BadRequest("Full name is required.");

        if (string.IsNullOrWhiteSpace(email))
            return LostFoundResult<SubAdminDto>.BadRequest(
                "Official email / username is required.");

        if (!email.Contains('@') || email.StartsWith('@') || email.EndsWith('@'))
            return LostFoundResult<SubAdminDto>.BadRequest(
                "Enter a valid official email address.");

        if (string.IsNullOrWhiteSpace(password))
            return LostFoundResult<SubAdminDto>.BadRequest("Initial password is required.");

        if (password.Length < 8)
            return LostFoundResult<SubAdminDto>.BadRequest(
                "Initial password must be at least 8 characters.");

        var emailLower = email.ToLowerInvariant();

        // Include deleted accounts: the login route resolves by email with
        // FirstOrDefault, so a second row with the same address would make
        // future sign-ins ambiguous even if one row is soft-deleted.
        var duplicate = await _db.Users.AnyAsync(
            u => u.Email != null && u.Email.Trim().ToLower() == emailLower,
            ct);

        if (duplicate)
            return LostFoundResult<SubAdminDto>.Conflict(
                "This email / username is already used by another account.");

        var user = new User
        {
            IdNumber = $"LF-ADM-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            FullName = fullName,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = LostFoundRoles.SubAdmin,
            AdminModule = Module,
            IsVerified = true,
            IsProfileComplete = true,
            AccountStatus = "Active",
            // Forces a password change on first sign-in. The console blocks
            // every management call until /api/profile/change-password clears it.
            MustChangePassword = true,
            CreatedAt = DateTime.Now,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        await _audit.StageAsync(
            actorUserId,
            "Administrator Created",
            "Administrator",
            user.Id,
            null,
            "Active",
            $"Created Sub-Admin account for {user.FullName} ({user.Email}). Initial password change required.",
            ct);

        await _db.SaveChangesAsync(ct);

        return LostFoundResult<SubAdminDto>.Created(new SubAdminDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role,
            RoleLabel = LostFoundRoles.Label(user.Role),
            IsActive = true,
            MustChangePassword = true,
            CreatedAt = user.CreatedAt,
        }, "Sub-Admin account created. The initial password must be changed at first sign-in.");
    }

    public async Task<LostFoundResult<SubAdminDto>> SetAdminStatusAsync(
        int actorUserId, int targetUserId, bool isActive, CancellationToken ct = default)
    {
        if (!await CanManageAsync(actorUserId, ct))
            return LostFoundResult<SubAdminDto>.Forbidden(
                "Only a Super Admin can activate or deactivate administrator accounts.");

        if (targetUserId == actorUserId)
            return LostFoundResult<SubAdminDto>.BadRequest(
                "You cannot change the status of your own account.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
        if (user is null)
            return LostFoundResult<SubAdminDto>.NotFound("Administrator account not found.");

        if (!LostFoundRoles.IsLostFoundRole(user.Role))
            return LostFoundResult<SubAdminDto>.BadRequest(
                "Only Lost & Found administrator accounts can be managed here.");

        // A Super Admin can never be deactivated from this screen: there must
        // always be someone able to reopen the console.
        if (LostFoundRoles.IsSuperRole(user.Role))
            return LostFoundResult<SubAdminDto>.Forbidden(
                "A Super Admin account cannot be changed from the Sub-Admin screen.");

        var previous = user.AccountStatus ?? "Unknown";
        user.AccountStatus = isActive ? "Active" : "Inactive";

        if (!isActive)
        {
            // Kill every token minted before now, so a deactivated account
            // cannot keep working for the rest of its eight-hour lifetime.
            user.TokensValidFrom = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

            var sessions = await _db.UserSessions
                .Where(s => s.UserId == targetUserId && s.LoggedOutAt == null)
                .ToListAsync(ct);

            foreach (var session in sessions)
            {
                session.LoggedOutAt = DateTime.UtcNow;
                session.LogoutReason = "Account deactivated";
                session.IsRevoked = true;
            }
        }

        await _audit.StageAsync(
            actorUserId,
            isActive ? "Administrator Activated" : "Administrator Deactivated",
            "Administrator",
            user.Id,
            previous,
            user.AccountStatus,
            $"{(isActive ? "Activated" : "Deactivated")} {user.FullName} ({user.Email}).",
            ct);

        await _db.SaveChangesAsync(ct);

        return LostFoundResult<SubAdminDto>.Ok(new SubAdminDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role,
            RoleLabel = LostFoundRoles.Label(user.Role),
            IsActive = isActive,
            MustChangePassword = user.MustChangePassword,
            CreatedAt = user.CreatedAt,
        }, isActive ? "Administrator activated." : "Administrator deactivated.");
    }

    // =========================================================
    // LOGIN ACTIVITY (SUPER ADMIN ONLY)
    // =========================================================

    public async Task<LostFoundResult<List<AdminLoginActivityDto>>> GetLoginActivityAsync(
        int actorUserId, string? currentSessionKey, CancellationToken ct = default)
    {
        if (!await CanManageAsync(actorUserId, ct))
            return LostFoundResult<List<AdminLoginActivityDto>>.Forbidden(
                "Only a Super Admin can view administrator login activity.");

        var since = DateTime.UtcNow.AddDays(-90);
        var now = DateTime.UtcNow;

        var rows = await (
            from s in _db.UserSessions.AsNoTracking()
            join u in _db.Users.AsNoTracking() on s.UserId equals u.Id
            where s.CreatedAt >= since &&
                  (u.Role == LostFoundRoles.SubAdmin ||
                   u.Role == LostFoundRoles.SuperAdmin ||
                   u.Role == LostFoundRoles.LegacyAdmin)
            orderby s.CreatedAt descending
            select new { Session = s, User = u })
            .Take(300)
            .ToListAsync(ct);

        var key = (currentSessionKey ?? string.Empty).Trim();

        return LostFoundResult<List<AdminLoginActivityDto>>.Ok(rows.Select(row =>
        {
            var s = row.Session;

            var expiresAt = s.ExpiresAt ?? s.CreatedAt.AddHours(8);

            var status = s.LoggedOutAt.HasValue
                ? "Logged Out"
                : s.IsRevoked || now >= expiresAt
                    ? "Session Expired"
                    : "Active";

            return new AdminLoginActivityDto
            {
                SessionId = s.UserSessionId,
                UserId = s.UserId,
                FullName = row.User.FullName,
                Email = row.User.Email,
                Role = row.User.Role,
                RoleLabel = LostFoundRoles.Label(row.User.Role),
                // Stored UTC -> mark as UTC before serialising.
                LoginAt = DateTime.SpecifyKind(s.CreatedAt, DateTimeKind.Utc),
                LogoutAt = s.LoggedOutAt.HasValue
                    ? DateTime.SpecifyKind(s.LoggedOutAt.Value, DateTimeKind.Utc)
                    : null,
                LastActivityAt = DateTime.SpecifyKind(s.LastSeenAt, DateTimeKind.Utc),
                SessionStatus = status,
                DeviceLabel = s.DeviceLabel,
                IpAddress = s.IpAddress,
                LogoutReason = s.LogoutReason,
                IsCurrentSession = key.Length > 0 && s.SessionKey == key,
            };
        }).ToList());
    }

    // =========================================================
    // ACTIVITY LOG (SUPER ADMIN ONLY, APPEND-ONLY)
    // =========================================================

    public async Task<LostFoundResult<AdminActivityPageDto>> GetActivityLogAsync(
        int actorUserId,
        int page = 1,
        int pageSize = 50,
        string? entityType = null,
        CancellationToken ct = default)
    {
        if (!await CanManageAsync(actorUserId, ct))
            return LostFoundResult<AdminActivityPageDto>.Forbidden(
                "Only a Super Admin can view the activity log.");

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.AdminActivityLogs
            .AsNoTracking()
            .Where(a => a.Module == LostFoundAuditService.ModuleName);

        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(a => a.EntityType == entityType.Trim());

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AdminActivityDto
            {
                Id = a.Id,
                ActorName = a.ActorName ?? "Unknown administrator",
                ActorRole = a.ActorRole ?? string.Empty,
                ActorRoleLabel = a.ActorRole == LostFoundRoles.SubAdmin
                    ? "Sub-Admin"
                    : a.ActorRole == LostFoundRoles.SuperAdmin ||
                      a.ActorRole == LostFoundRoles.LegacyAdmin ||
                      a.ActorRole == "Admin" ||
                      a.ActorRole == "SuperAdmin"
                        ? "Super Admin"
                        : (a.ActorRole ?? string.Empty),
                Action = a.Action,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                PreviousStatus = a.PreviousStatus,
                NewStatus = a.NewStatus,
                Details = a.Details,
                Timestamp = a.Timestamp,
            })
            .ToListAsync(ct);

        return LostFoundResult<AdminActivityPageDto>.Ok(new AdminActivityPageDto
        {
            Items = items,
            Total = total,
        });
    }

    // =========================================================
    // HELPERS
    // =========================================================

    private async Task<bool> CanManageAsync(int userId, CancellationToken ct)
    {
        if (userId <= 0) return false;

        var role = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Role)
            .FirstOrDefaultAsync(ct);

        return LostFoundRoles.IsSuperRole(role);
    }

    private static bool IsActive(string? status) =>
        string.IsNullOrWhiteSpace(status) ||
        string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);
}
