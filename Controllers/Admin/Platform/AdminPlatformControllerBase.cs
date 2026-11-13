using CDM_OneServe_API.Models.Platform;
using CDM_OneServe_API.Services.Admin.Platform;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin.Platform;

/// <summary>
/// Shared plumbing for every central admin endpoint.
///
/// Each route is protected twice:
///   1. [Authorize(Roles = "Admin,SuperAdmin")] - rejects everyone else.
///   2. <see cref="AuthorizeAsync"/> - re-resolves the capability from the
///      DATABASE, so a revoked permission or a suspended account stops
///      working before the 8-hour token expires.
///
/// The role attribute alone is never treated as sufficient for a privileged
/// action, and the frontend check is never the only check.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,SuperAdmin")]
public abstract class AdminPlatformControllerBase : ControllerBase
{
    protected readonly PermissionService Permissions;
    protected readonly AdminAuditService Audit;
    protected readonly SecurityEventService Security;

    protected AdminPlatformControllerBase(
        PermissionService permissions,
        AdminAuditService audit,
        SecurityEventService security)
    {
        Permissions = permissions;
        Audit = audit;
        Security = security;
    }

    protected async Task<AdminIdentity?> GetIdentityAsync(CancellationToken ct = default) =>
        await Permissions.ResolveAsync(User, ct);

    /// <summary>
    /// Resolves the caller and checks one capability. On denial it writes a
    /// security event and returns a 403 result - never a silent pass.
    /// </summary>
    protected async Task<PlatformAuth> AuthorizeAsync(
        string key,
        string minimum,
        CancellationToken ct = default)
    {
        var identity = await Permissions.ResolveAsync(User, ct);

        if (identity is null)
        {
            return new PlatformAuth(
                null,
                Unauthorized(new { message = "Your session is no longer valid. Please sign in again." }));
        }

        if (!identity.Can(key, minimum))
        {
            await Security.RecordFromRequestAsync(
                HttpContext,
                SecurityEventTypes.PrivilegedAccessDenied,
                SecuritySeverities.Warning,
                identity.UserId,
                null,
                null,
                $"Denied {key} (required level: {minimum})",
                ct);

            return new PlatformAuth(
                identity,
                StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = "You do not have permission to perform this action."
                }));
        }

        return new PlatformAuth(identity, null);
    }

    /// <summary>
    /// Writes one audit row for a completed admin action. Request metadata
    /// (IP, session key) is attached automatically.
    /// </summary>
    protected async Task WriteAuditAsync(
        string module,
        string category,
        string action,
        string? entityType = null,
        int? entityId = null,
        string? previousStatus = null,
        string? newStatus = null,
        string? details = null,
        string? reason = null,
        string? previousValue = null,
        string? newValue = null,
        CancellationToken ct = default)
    {
        var row = await Audit.StageAsync(
            User, module, category, action, entityType, entityId,
            previousStatus, newStatus, details, reason, previousValue, newValue, ct);

        AdminAuditService.AttachRequest(row, HttpContext);

        await Audit.SaveAsync(ct);
    }

    protected static string? Trunc(string? value, int max) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Length <= max
                ? value.Trim()
                : value.Trim()[..max];
}

public sealed record PlatformAuth(AdminIdentity? Identity, IActionResult? Failure)
{
    public bool Succeeded => Failure is null && Identity is not null;
}
