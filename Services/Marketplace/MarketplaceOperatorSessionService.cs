using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Models.Marketplace;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// The validated identity of one duty shift: which workspace is operated and
/// the HUMAN name entered after login.
///
/// Two cases, kept apart on purpose:
///
///   STALL ACCOUNT - a login bound to exactly one workspace
///   (marketplace_stall_accounts). The workspace is derived server-side from
///   the link. Sending any other workspace id is rejected, so a stall can
///   never reach across into another stall's products, inventory or orders.
///
///   FLOATING OPERATOR - the Head or a legacy personal staff account, with no
///   fixed workspace. They choose a workspace when the session starts (the
///   setup screen's dropdown), and the session pins them to it.
///
/// The operator name on every audit row comes from the validated session,
/// never from client input.
/// </summary>
public sealed record MarketplaceOperatorAttribution(
    int WorkspaceId,
    string WorkspaceName,
    string OperatorName,
    string? IdLast3,
    Guid SessionId);

public class MarketplaceOperatorSessionService
{
    /// <summary>How long a duty shift lasts before the name must be re-entered.</summary>
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

    private readonly AppDbContext _db;
    private readonly MarketplaceAccessService _access;

    public MarketplaceOperatorSessionService(AppDbContext db, MarketplaceAccessService access)
    {
        _db = db;
        _access = access;
    }

    // ==========================================================
    // workspace resolution
    // ==========================================================

    /// <summary>
    /// The fixed workspace of a stall login account, or null for floating
    /// operators. Inactive links and inactive workspaces resolve to null, so
    /// a retired stall account loses its workspace the moment it is retired.
    /// </summary>
    public async Task<MarketplaceWorkspace?> GetLinkedWorkspaceAsync(
        int userId,
        CancellationToken ct = default)
    {
        var ws = await _db.MarketplaceStallAccounts.AsNoTracking()
            .Where(a => a.UserId == userId && a.IsActive)
            .Select(a => a.Workspace)
            .FirstOrDefaultAsync(ct);

        if (ws is null || !ws.IsActive) return null;

        await _db.Entry(ws).Reference(w => w.StallLocation).LoadAsync(ct);

        return ws;
    }

    public Task<bool> IsStallAccountAsync(int userId, CancellationToken ct = default) =>
        _db.MarketplaceStallAccounts.AsNoTracking()
            .AnyAsync(a => a.UserId == userId && a.IsActive, ct);

    private async Task<MarketplaceWorkspace?> LoadWorkspaceAsync(
        int workspaceId,
        bool activeOnly,
        CancellationToken ct)
    {
        var query = _db.MarketplaceWorkspaces.AsNoTracking()
            .Include(w => w.StallLocation)
            .Where(w => w.WorkspaceId == workspaceId);

        if (activeOnly)
            query = query.Where(w => w.IsActive);

        return await query.FirstOrDefaultAsync(ct);
    }

    // ==========================================================
    // sessions
    // ==========================================================

    /// <summary>The caller's currently active session, if any.</summary>
    public async Task<MarketplaceOperatorSession?> GetActiveAsync(
        int userId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        return await _db.MarketplaceOperatorSessions
            .Include(s => s.Workspace)
            .Where(s => s.UserId == userId && s.EndedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<MarketplaceResult<MarketplaceOperatorSessionDto>> StartAsync(
        int userId,
        StartOperatorSessionRequest request,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(userId, ct);
        if (actor is null)
            return MarketplaceResult<MarketplaceOperatorSessionDto>.Forbidden(
                "Marketplace Staff access required.");

        var name = (request.OperatorName ?? "").Trim();
        if (name.Length < 2 || name.Length > 120)
            return MarketplaceResult<MarketplaceOperatorSessionDto>.BadRequest(
                "Enter the name of the operator on duty (2-120 characters).");

        MarketplaceWorkspace? workspace;
        var linked = await GetLinkedWorkspaceAsync(userId, ct);

        if (linked is not null)
        {
            // A stall login operates its own workspace. A different id in the
            // request is not a choice - it is rejected.
            if (request.WorkspaceId.HasValue && request.WorkspaceId.Value != linked.WorkspaceId)
                return MarketplaceResult<MarketplaceOperatorSessionDto>.Forbidden(
                    "This account operates its own stall only.");

            workspace = linked;
        }
        else
        {
            if (!request.WorkspaceId.HasValue)
                return MarketplaceResult<MarketplaceOperatorSessionDto>.BadRequest(
                    "Select the stall or workspace for this shift.");

            workspace = await LoadWorkspaceAsync(request.WorkspaceId.Value, activeOnly: true, ct);

            if (workspace is null)
                return MarketplaceResult<MarketplaceOperatorSessionDto>.BadRequest(
                    "That workspace is not available.");
        }

        // One active shift per account: starting a new one ends the old.
        var now = DateTime.UtcNow;
        await _db.MarketplaceOperatorSessions
            .Where(s => s.UserId == userId && s.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, now), ct);

        // When the entered name matches the designated operator, the shift
        // carries their ID suffix into every audit row. Otherwise the name
        // stands alone - matched, never trusted blindly, never rejected.
        string? idLast3 = null;
        var designated = await _db.MarketplaceStallAccounts.AsNoTracking()
            .Where(a => a.UserId == userId && a.IsActive &&
                        a.OperatorName != null && a.IdLast3 != null)
            .Select(a => new { a.OperatorName, a.IdLast3 })
            .FirstOrDefaultAsync(ct);

        if (designated is not null &&
            string.Equals(designated.OperatorName?.Trim(), name, StringComparison.OrdinalIgnoreCase))
            idLast3 = designated.IdLast3;

        var session = new MarketplaceOperatorSession
        {
            SessionId = Guid.NewGuid(),
            UserId = userId,
            WorkspaceId = workspace.WorkspaceId,
            OperatorName = name,
            IdLast3 = idLast3,
            CreatedAt = now,
            ExpiresAt = now.Add(SessionLifetime),
        };

        _db.MarketplaceOperatorSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<MarketplaceOperatorSessionDto>.Ok(ToDto(session, workspace));
    }

    /// <summary>
    /// Ends the caller's active session (or one specific session). Switching
    /// workspace is end + start, so the old name never leaks into the new one.
    /// </summary>
    public async Task<MarketplaceResult<bool>> EndAsync(
        int userId,
        Guid? sessionId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var query = _db.MarketplaceOperatorSessions
            .Where(s => s.UserId == userId && s.EndedAt == null);

        if (sessionId.HasValue)
            query = query.Where(s => s.SessionId == sessionId.Value);

        await query.ExecuteUpdateAsync(
            s => s.SetProperty(x => x.EndedAt, now), ct);

        return MarketplaceResult<bool>.Ok(true, "Operator session ended.");
    }

    /// <summary>
    /// Validates the session id the client sent with a mutation. Returns the
    /// session the operator name must be taken from, or null.
    /// </summary>
    public async Task<MarketplaceOperatorSession?> ValidateAsync(
        int userId,
        Guid? sessionId,
        CancellationToken ct = default)
    {
        if (!sessionId.HasValue || sessionId.Value == Guid.Empty)
            return null;

        var now = DateTime.UtcNow;

        return await _db.MarketplaceOperatorSessions
            .Include(s => s.Workspace)
            .Where(s => s.SessionId == sessionId.Value &&
                        s.UserId == userId &&
                        s.EndedAt == null &&
                        s.ExpiresAt > now)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Resolves which workspace a staff READ is scoped to. Null means global
    /// (Head or legacy personal staff with no session and no filter) - kept
    /// so existing oversight screens keep working during the transition.
    /// Stall accounts are always scoped to their own workspace.
    /// </summary>
    public async Task<(int? WorkspaceId, string? Error)> ResolveReadScopeAsync(
        int userId,
        Guid? sessionId,
        int? requestedWorkspaceId,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(userId, ct);
        if (actor is null) return (null, "Marketplace Staff access required.");

        var linked = await GetLinkedWorkspaceAsync(userId, ct);
        if (linked is not null)
        {
            if (requestedWorkspaceId.HasValue && requestedWorkspaceId.Value != linked.WorkspaceId)
                return (null, "This account operates its own stall only.");

            return (linked.WorkspaceId, null);
        }

        var session = await ValidateAsync(userId, sessionId, ct);
        if (session is not null)
        {
            if (requestedWorkspaceId.HasValue && requestedWorkspaceId.Value != session.WorkspaceId)
                return (null, "This session is pinned to another workspace.");

            return (session.WorkspaceId, null);
        }

        if (!requestedWorkspaceId.HasValue)
            return (null, null);

        var workspace = await LoadWorkspaceAsync(requestedWorkspaceId.Value, activeOnly: false, ct);
        return workspace is null
            ? (null, "That workspace does not exist.")
            : ((int?)workspace.WorkspaceId, null);
    }

    /// <summary>
    /// Resolves the workspace a staff MUTATION must stay inside, from the
    /// validated session. Mutations always require a session - that is what
    /// guarantees an operator name for the audit row.
    /// </summary>
    public async Task<(MarketplaceOperatorAttribution? Attribution, string? Error)> RequireMutationScopeAsync(
        int userId,
        Guid? sessionId,
        int? targetWorkspaceId,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(userId, ct);
        if (actor is null)
            return (null, "Marketplace Staff access required.");

        var session = await ValidateAsync(userId, sessionId, ct);
        if (session is null)
            return (null, "Start an operator session first: enter the operator name.");

        if (targetWorkspaceId.HasValue && targetWorkspaceId.Value != session.WorkspaceId)
            return (null, "That belongs to another workspace.");

        var workspace = session.Workspace ??
            await LoadWorkspaceAsync(session.WorkspaceId, activeOnly: true, ct);

        if (workspace is null)
            return (null, "That workspace is no longer available.");

        return (new MarketplaceOperatorAttribution(
            workspace.WorkspaceId,
            workspace.Name,
            session.OperatorName,
            session.IdLast3,
            session.SessionId), null);
    }

    public static MarketplaceOperatorSessionDto ToDto(
        MarketplaceOperatorSession session,
        MarketplaceWorkspace? workspace) => new()
        {
            SessionId = session.SessionId,
            WorkspaceId = session.WorkspaceId,
            WorkspaceName = workspace?.Name ?? session.Workspace?.Name ?? "",
            WorkspaceType = workspace?.WorkspaceType ?? session.Workspace?.WorkspaceType ?? "",
            StallLocationName = workspace?.StallLocation?.Name
                ?? session.Workspace?.StallLocation?.Name ?? "",
            OperatorName = session.OperatorName,
            IdLast3 = session.IdLast3 ?? "",
            CreatedAt = session.CreatedAt,
            ExpiresAt = session.ExpiresAt,
        };
}
