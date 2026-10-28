using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.LostFound;

/// <summary>Snapshot of the signed-in administrator at the moment of an action.</summary>
public sealed record LostFoundActor(
    int Id,
    string FullName,
    string Role,
    string RoleLabel,
    bool IsAdmin,
    bool CanManageAdmins);

/// <summary>
/// Append-only audit writer for the Lost &amp; Found module.
///
/// The actor is always resolved from the authenticated user id the
/// controller read from the JWT - never from the request body. The row is
/// added to the shared <c>admin_activity_logs</c> table with a Module marker
/// so Lost &amp; Found queries never see Library rows, and the other modules
/// never see ours.
///
/// This service never calls SaveChanges: the caller commits the business
/// change and the audit row in one transaction-like save, so an action can
/// never succeed without its audit entry.
/// </summary>
public class LostFoundAuditService
{
    public const string ModuleName = "LostFound";

    private readonly AppDbContext _db;

    public LostFoundAuditService(AppDbContext db) => _db = db;

    /// <summary>Loads the actor snapshot from the database (not the client).</summary>
    public async Task<LostFoundActor?> GetActorAsync(int userId, CancellationToken ct = default)
    {
        if (userId <= 0) return null;

        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.FullName, u.Role })
            .FirstOrDefaultAsync(ct);

        if (user is null) return null;

        var isAdmin = LostFoundRoles.IsLostFoundRole(user.Role);
        var canManage = LostFoundRoles.IsSuperRole(user.Role);

        return new LostFoundActor(
            user.Id,
            string.IsNullOrWhiteSpace(user.FullName) ? "Administrator" : user.FullName.Trim(),
            user.Role ?? string.Empty,
            LostFoundRoles.Label(user.Role),
            isAdmin,
            canManage);
    }

    /// <summary>
    /// Adds one audit row. Returns the row so callers can save it together
    /// with the business change.
    /// </summary>
    public async Task<AdminActivityLog> StageAsync(
        int actorUserId,
        string action,
        string entityType,
        int? entityId,
        string? previousStatus,
        string? newStatus,
        string? details = null,
        CancellationToken ct = default)
    {
        var actor = await GetActorAsync(actorUserId, ct);

        var row = new AdminActivityLog
        {
            UserId = actorUserId,
            Action = action,
            Timestamp = DateTime.UtcNow.AddHours(8), // PH wall clock, like LF records
            Module = ModuleName,
            ActorName = actor?.FullName ?? "Unknown administrator",
            ActorRole = actor?.Role,
            EntityType = entityType,
            EntityId = entityId,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            Details = details,
        };

        _db.AdminActivityLogs.Add(row);
        return row;
    }
}
