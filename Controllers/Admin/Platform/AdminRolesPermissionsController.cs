using CDM_OneServe_API.Models.Platform;
using CDM_OneServe_API.Services.Admin.Platform;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin.Platform;

/// <summary>
/// Role and permission management (Part 4).
///
/// The matrix is database-backed and enforced on every central admin route.
/// Module-owner consoles keep their own server-side role rules - this page
/// never weakens them, it only governs the platform-admin surface and which
/// services a platform admin may open.
///
/// Reads need <c>admin.roles.manage</c> at view level; writes need manage
/// level. Every write is audited with the previous and new level.
/// </summary>
[Route("api/admin/roles-permissions")]
public class AdminRolesPermissionsController : AdminPlatformControllerBase
{
    private const string Module = "Roles";
    private const string Category = "Role and Permission Management";

    public AdminRolesPermissionsController(
        PermissionService permissions,
        AdminAuditService audit,
        SecurityEventService security)
        : base(permissions, audit, security)
    {
    }

    // GET: api/admin/roles-permissions
    [HttpGet]
    public async Task<IActionResult> GetOverview(CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.RolesManage, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var roles = await Permissions.GetRolesAsync(ct);
        var catalog = PermissionCatalog.All;

        return Ok(new
        {
            roles,
            knownRoles = PermissionService.KnownRoles,
            permissionCatalog = catalog.Select(x => new
            {
                key = x.Key,
                label = x.Label,
                category = x.Category,
                description = x.Description,
                x.SuperAdminOnly,
                x.DefaultAdmin,
                x.DefaultSuperAdmin
            }),
            levels = new[] { "none", "view", "manage", "full_control" },
            me = new
            {
                auth.Identity!.UserId,
                auth.Identity.Role,
                auth.Identity.FullName,
                isSuperAdmin = auth.Identity.IsSuperAdmin,
                levels = auth.Identity.Levels
            }
        });
    }

    // GET: api/admin/roles-permissions/me
    /// <summary>What the signed-in admin can do - used by the sidebar.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var identity = await GetIdentityAsync(ct);

        if (identity is null)
        {
            return Unauthorized(new { message = "Your session is no longer valid." });
        }

        return Ok(new
        {
            identity.UserId,
            identity.FullName,
            identity.Role,
            identity.AccountStatus,
            isSuperAdmin = identity.IsSuperAdmin,
            permissions = identity.Levels
        });
    }

    // GET: api/admin/roles-permissions/roles/{role}
    [HttpGet("roles/{role}")]
    public async Task<IActionResult> GetRole(string role, CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.RolesManage, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        if (string.IsNullOrWhiteSpace(role))
        {
            return BadRequest(new { message = "A role is required." });
        }

        var matrix = await Permissions.GetRoleMatrixAsync(role, ct);
        var members = await Permissions.GetRoleMembersAsync(role, ct);

        return Ok(new
        {
            role,
            isModuleRole = role is not ("Admin" or "SuperAdmin"),
            permissions = matrix,
            users = members,
            userCount = members.Count
        });
    }

    // PUT: api/admin/roles-permissions/roles/{role}
    [HttpPut("roles/{role}")]
    public async Task<IActionResult> UpdateRole(
        string role,
        [FromBody] RolePermissionRequest request,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.RolesManage, PermissionLevels.Manage, ct);

        if (auth.Failure is not null) return auth.Failure;

        if (request is null)
        {
            return BadRequest(new { message = "A permission change is required." });
        }

        var current = await Permissions.GetRoleMatrixAsync(role, ct);
        var previous = current.FirstOrDefault(x => x.Key == request.Key);

        var error = await Permissions.SetRoleLevelAsync(
            role, request.Key, request.Level, auth.Identity!.UserId, ct);

        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        await WriteAuditAsync(
            Module, Category,
            $"Changed {previous?.Label ?? request.Key} for {role}",
            entityType: "Role",
            details: request.Reason,
            reason: request.Reason,
            previousValue: previous?.Level,
            newValue: request.Level,
            ct: ct);

        return Ok(new
        {
            message = "Permission updated.",
            role,
            key = request.Key,
            level = request.Level
        });
    }

    // GET: api/admin/roles-permissions/users/{userId}/overrides
    [HttpGet("users/{userId:int}/overrides")]
    public async Task<IActionResult> GetUserOverrides(int userId, CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.RolesManage, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var overrides = await Permissions.GetUserOverridesAsync(userId, ct);

        return Ok(new { userId, overrides });
    }

    // PUT: api/admin/roles-permissions/users/{userId}/overrides
    [HttpPut("users/{userId:int}/overrides")]
    public async Task<IActionResult> SetUserOverride(
        int userId,
        [FromBody] UserPermissionRequest request,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.RolesManage, PermissionLevels.Manage, ct);

        if (auth.Failure is not null) return auth.Failure;

        if (request is null)
        {
            return BadRequest(new { message = "A permission change is required." });
        }

        var error = await Permissions.SetUserOverrideAsync(
            userId,
            request.Key,
            request.Level,
            request.Reason,
            auth.Identity!.UserId,
            request.ExpiresAt,
            ct);

        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        await WriteAuditAsync(
            Module, Category,
            $"Set a per-user permission ({request.Key})",
            entityType: "User",
            entityId: userId,
            details: request.Reason,
            reason: request.Reason,
            newValue: request.Level,
            ct: ct);

        return Ok(new { message = "User permission saved." });
    }

    // DELETE: api/admin/roles-permissions/users/{userId}/overrides/{key}
    [HttpDelete("users/{userId:int}/overrides/{key}")]
    public async Task<IActionResult> RemoveUserOverride(
        int userId,
        string key,
        [FromQuery] string? reason,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.RolesManage, PermissionLevels.Manage, ct);

        if (auth.Failure is not null) return auth.Failure;

        var removed = await Permissions.RemoveUserOverrideAsync(userId, key, ct);

        if (!removed)
        {
            return NotFound(new { message = "That override does not exist." });
        }

        await WriteAuditAsync(
            Module, Category,
            $"Removed a per-user permission override ({key})",
            entityType: "User",
            entityId: userId,
            details: reason,
            reason: reason,
            previousValue: key,
            newValue: "removed",
            ct: ct);

        return Ok(new { message = "Override removed." });
    }
}

public sealed record RolePermissionRequest(
    string Key,
    string Level,
    string? Reason);

public sealed record UserPermissionRequest(
    string Key,
    string Level,
    string Reason,
    DateTime? ExpiresAt);
