using System.Security.Claims;

using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models.Platform;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Platform;

/// <summary>
/// The signed-in administrator's resolved capabilities.
///
/// Resolved from the DATABASE on every request, not from the JWT: the token
/// lives for 8 hours, and a revoked permission must take effect immediately.
/// </summary>
public sealed record AdminIdentity(
    int UserId,
    string FullName,
    string Role,
    string AccountStatus,
    IReadOnlyDictionary<string, string> Levels)
{
    public bool IsActive =>
        string.Equals(AccountStatus, "Active", StringComparison.OrdinalIgnoreCase);

    public bool IsSuperAdmin =>
        string.Equals(Role, PermissionCatalog.SuperAdminRole, StringComparison.Ordinal);

    public string LevelOf(string key) =>
        Levels.TryGetValue(key, out var level) ? level : PermissionLevels.None;

    /// <summary>
    /// True only when the account is Active and holds at least
    /// <paramref name="minimum"/>. This is the check every central admin
    /// endpoint performs; it is never the only check on a module endpoint.
    /// </summary>
    public bool Can(string key, string minimum) =>
        IsActive &&
        PermissionLevels.Rank(LevelOf(key)) >= PermissionLevels.Rank(minimum);
}

/// <summary>
/// Reads and writes the platform permission matrix.
///
/// Storage is sparse: only rows an admin has changed exist. A missing row
/// falls back to the built-in default for Admin / SuperAdmin, and to "none"
/// for every other role - those roles are governed by their own consoles.
/// </summary>
public class PermissionService
{
    private readonly AppDbContext _db;

    public PermissionService(AppDbContext db) => _db = db;

    // ---------------------------------------------------------
    // RESOLVE
    // ---------------------------------------------------------

    public async Task<AdminIdentity?> ResolveAsync(
        ClaimsPrincipal principal,
        CancellationToken ct = default)
    {
        var raw = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!int.TryParse(raw, out var userId) || userId <= 0)
        {
            return null;
        }

        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.FullName, u.Role, u.AccountStatus })
            .FirstOrDefaultAsync(ct);

        if (user is null) return null;

        var roleRows = await _db.RolePermissions
            .AsNoTracking()
            .Where(x => x.Role == user.Role)
            .Select(x => new { x.PermissionKey, x.Level })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        var overrides = await _db.UserPermissionOverrides
            .AsNoTracking()
            .Where(x => x.UserId == userId &&
                        (x.ExpiresAt == null || x.ExpiresAt > now))
            .Select(x => new { x.PermissionKey, x.Level })
            .ToListAsync(ct);

        var roleLevels = roleRows
            .GroupBy(x => x.PermissionKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Level, StringComparer.Ordinal);

        var overrideLevels = overrides
            .GroupBy(x => x.PermissionKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Level, StringComparer.Ordinal);

        var levels = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var definition in PermissionCatalog.All)
        {
            var level = PermissionCatalog.DefaultLevel(user.Role, definition.Key);

            if (roleLevels.TryGetValue(definition.Key, out var roleLevel))
            {
                level = roleLevel;
            }

            // A per-user override is authoritative in both directions: it can
            // grant a capability or remove one the role would otherwise hold.
            if (overrideLevels.TryGetValue(definition.Key, out var overrideLevel))
            {
                level = overrideLevel;
            }

            // Destructive platform powers stay out of the Admin role no
            // matter what a stale row says. SuperAdmin only, by design.
            if (definition.SuperAdminOnly &&
                !string.Equals(user.Role, PermissionCatalog.SuperAdminRole,
                    StringComparison.Ordinal))
            {
                level = PermissionLevels.None;
            }

            levels[definition.Key] = PermissionLevels.IsKnown(level)
                ? level
                : PermissionLevels.None;
        }

        return new AdminIdentity(
            user.Id,
            string.IsNullOrWhiteSpace(user.FullName) ? "Administrator" : user.FullName.Trim(),
            user.Role ?? string.Empty,
            user.AccountStatus ?? "Unknown",
            levels);
    }

    public async Task<bool> CanAsync(
        ClaimsPrincipal principal,
        string key,
        string minimum,
        CancellationToken ct = default)
    {
        var identity = await ResolveAsync(principal, ct);
        return identity is not null && identity.Can(key, minimum);
    }

    // ---------------------------------------------------------
    // MATRIX (read)
    // ---------------------------------------------------------

    /// <summary>Every role that has an account, with its user count.</summary>
    public async Task<List<RoleSummary>> GetRolesAsync(CancellationToken ct = default)
    {
        var counts = await _db.Users
            .AsNoTracking()
            .Where(u => u.AccountStatus != "Deleted")
            .GroupBy(u => u.Role)
            .Select(g => new { Role = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var roleRows = await _db.RolePermissions
            .AsNoTracking()
            .ToListAsync(ct);

        return counts
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Role)
            .Select(x =>
            {
                var definition = string.Equals(x.Role, PermissionCatalog.SuperAdminRole,
                    StringComparison.Ordinal)
                    ? "Platform Super Administrator"
                    : string.Equals(x.Role, PermissionCatalog.AdminRole, StringComparison.Ordinal)
                        ? "Platform Administrator"
                        : $"{x.Role} (module role - governed by its own console)";

                return new RoleSummary(
                    x.Role,
                    definition,
                    x.Count,
                    roleRows.Count(r => r.Role == x.Role));
            })
            .ToList();
    }

    /// <summary>
    /// The effective matrix for one role: every catalog key, the level in
    /// force, and whether it comes from the database or the built-in default.
    /// </summary>
    public async Task<List<RolePermissionRow>> GetRoleMatrixAsync(
        string role,
        CancellationToken ct = default)
    {
        var rows = await _db.RolePermissions
            .AsNoTracking()
            .Where(x => x.Role == role)
            .ToListAsync(ct);

        return PermissionCatalog.All.Select(definition =>
        {
            var stored = rows.FirstOrDefault(x => x.PermissionKey == definition.Key);

            var level = stored?.Level
                ?? PermissionCatalog.DefaultLevel(role, definition.Key);

            if (definition.SuperAdminOnly &&
                !string.Equals(role, PermissionCatalog.SuperAdminRole,
                    StringComparison.Ordinal))
            {
                level = PermissionLevels.None;
            }

            return new RolePermissionRow(
                definition.Key,
                definition.Label,
                definition.Category,
                definition.Description,
                level,
                stored is not null ? "custom" : "default",
                definition.SuperAdminOnly);
        }).ToList();
    }

    /// <summary>The users holding a role, for the "assigned users" panel.</summary>
    public async Task<List<RoleMemberRow>> GetRoleMembersAsync(
        string role,
        CancellationToken ct = default)
    {
        return await _db.Users
            .AsNoTracking()
            .Where(u => u.Role == role && u.AccountStatus != "Deleted")
            .OrderBy(u => u.FullName)
            .Select(u => new RoleMemberRow(
                u.Id,
                u.FullName,
                u.Email,
                u.IdNumber,
                u.AccountStatus,
                u.AdminModule,
                u.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<List<UserOverrideRow>> GetUserOverridesAsync(
        int userId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        return await _db.UserPermissionOverrides
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.PermissionKey)
            .Select(x => new UserOverrideRow(
                x.Id,
                x.PermissionKey,
                x.Level,
                x.Reason,
                x.CreatedAt,
                x.ExpiresAt,
                x.ExpiresAt != null && x.ExpiresAt <= now))
            .ToListAsync(ct);
    }

    // ---------------------------------------------------------
    // MATRIX (write)
    // ---------------------------------------------------------

    /// <summary>
    /// Sets one role/key level. Returns null on success, or a reason string.
    /// Callers must write the audit row - this method never does.
    /// </summary>
    public async Task<string?> SetRoleLevelAsync(
        string role,
        string key,
        string level,
        int actorUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(role))
            return "A role is required.";

        var definition = PermissionCatalog.Find(key);
        if (definition is null)
            return $"Unknown permission '{key}'.";

        if (!PermissionLevels.IsKnown(level))
            return "Level must be one of: none, view, manage, full_control.";

        if (definition.SuperAdminOnly &&
            !string.Equals(role, PermissionCatalog.SuperAdminRole, StringComparison.Ordinal))
        {
            return "This capability is reserved for SuperAdmin and cannot be granted to another role.";
        }

        if (!string.Equals(role, PermissionCatalog.AdminRole, StringComparison.Ordinal) &&
            !string.Equals(role, PermissionCatalog.SuperAdminRole, StringComparison.Ordinal))
        {
            return "Only the Admin and SuperAdmin roles can be edited here. Module roles are managed inside their own console.";
        }

        if (string.Equals(role, PermissionCatalog.SuperAdminRole, StringComparison.Ordinal) &&
            level != PermissionLevels.FullControl)
        {
            return "The SuperAdmin role always keeps full control. Grant an exception to a specific account instead.";
        }

        var row = await _db.RolePermissions
            .FirstOrDefaultAsync(x => x.Role == role && x.PermissionKey == key, ct);

        if (row is null)
        {
            row = new RolePermission
            {
                Role = role,
                PermissionKey = key
            };
            _db.RolePermissions.Add(row);
        }

        row.Level = level;
        row.UpdatedByUserId = actorUserId;
        row.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Grants or removes one capability for one account.</summary>
    public async Task<string?> SetUserOverrideAsync(
        int userId,
        string key,
        string level,
        string reason,
        int actorUserId,
        DateTime? expiresAt,
        CancellationToken ct = default)
    {
        if (PermissionCatalog.Find(key) is null)
            return $"Unknown permission '{key}'.";

        if (!PermissionLevels.IsKnown(level))
            return "Level must be one of: none, view, manage, full_control.";

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            return "A reason (at least 5 characters) is required for a per-user permission change.";

        var definition = PermissionCatalog.Find(key)!;

        var target = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Role, u.AccountStatus })
            .FirstOrDefaultAsync(ct);

        if (target is null) return "The user could not be found.";

        if (definition.SuperAdminOnly &&
            level != PermissionLevels.None &&
            !string.Equals(target.Role, PermissionCatalog.SuperAdminRole, StringComparison.Ordinal))
        {
            return "This capability is reserved for SuperAdmin accounts.";
        }

        var row = await _db.UserPermissionOverrides
            .FirstOrDefaultAsync(x => x.UserId == userId && x.PermissionKey == key, ct);

        if (row is null)
        {
            row = new UserPermissionOverride
            {
                UserId = userId,
                PermissionKey = key
            };
            _db.UserPermissionOverrides.Add(row);
        }

        row.Level = level;
        row.Reason = reason.Trim();
        row.GrantedByUserId = actorUserId;
        row.CreatedAt = DateTime.UtcNow;
        row.ExpiresAt = expiresAt;

        await _db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<bool> RemoveUserOverrideAsync(
        int userId,
        string key,
        CancellationToken ct = default)
    {
        var row = await _db.UserPermissionOverrides
            .FirstOrDefaultAsync(x => x.UserId == userId && x.PermissionKey == key, ct);

        if (row is null) return false;

        _db.UserPermissionOverrides.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Every known role, for the role picker (even with 0 users).</summary>
    public static IReadOnlyList<string> KnownRoles { get; } = new[]
    {
        "SuperAdmin", "Admin",
        "LibraryAdmin", "LibraryStaff",
        "GuidanceAdmin", "Counselor",
        "LostFoundAdmin", "LostFoundSuperAdmin", "LostFoundSubAdmin",
        "ClinicAdmin",
        "Faculty", "Student"
    };
}

public sealed record RoleSummary(string Role, string Description, int UserCount, int CustomPermissionCount);

public sealed record RolePermissionRow(
    string Key,
    string Label,
    string Category,
    string Description,
    string Level,
    string Source,
    bool SuperAdminOnly);

public sealed record RoleMemberRow(
    int Id,
    string FullName,
    string Email,
    string IdNumber,
    string AccountStatus,
    string? AdminModule,
    DateTime CreatedAt);

public sealed record UserOverrideRow(
    int Id,
    string PermissionKey,
    string Level,
    string Reason,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    bool IsExpired);
