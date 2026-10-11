using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// The operator session: the duty shift behind a stall login.
///
///   GET    /api/marketplace/staff/session
///   POST   /api/marketplace/staff/session
///   POST   /api/marketplace/staff/session/end
///
/// After signing in with the stall account, the human on duty enters their
/// name here once. Mutations (inventory, products, order status) require the
/// returned session id in the X-Operator-Session header, and the audit trail
/// takes the operator name from the validated session - never from the request.
/// </summary>
[ApiController]
[Route("api/marketplace/staff/session")]
[Authorize]
public class MarketplaceOperatorSessionController : MarketplaceControllerBase
{
    private readonly MarketplaceOperatorSessionService _sessions;

    public MarketplaceOperatorSessionController(MarketplaceOperatorSessionService sessions)
    {
        _sessions = sessions;
    }

    /// <summary>
    /// The caller's active shift, or null when no session is open. The gate
    /// calls this right after auth to decide between dashboard and setup.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        var session = await _sessions.GetActiveAsync(userId, ct);

        if (session is null)
            return Ok(ApiResponse.Ok<object?>(null, "No active operator session."));

        var workspace = await _sessions.GetLinkedWorkspaceAsync(userId, ct);

        return Ok(ApiResponse.Ok(
            MarketplaceOperatorSessionService.ToDto(session, workspace ?? session.Workspace),
            "Operator session active."));
    }

    /// <summary>
    /// Opens a shift. Stall accounts operate their fixed workspace; floating
    /// operators (Head, legacy personal staff) choose one in the request.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Start(
        [FromBody] StartOperatorSessionRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _sessions.StartAsync(userId, request, ct));
    }

    /// <summary>
    /// Ends the shift. Switching workspace is end + start, so the old name
    /// never leaks into the new workspace's audit rows.
    /// </summary>
    [HttpPost("end")]
    public async Task<IActionResult> End(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _sessions.EndAsync(userId, OperatorSession(), ct));
    }

    /// <summary>
    /// Which workspace this login is bound to, if any. Stall accounts get
    /// their fixed workspace (the setup screen shows it read-only); floating
    /// operators get null and must pick from the workspace list.
    /// </summary>
    [HttpGet("context")]
    public async Task<IActionResult> Context(
        [FromServices] AppDbContext db,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        var workspace = await _sessions.GetLinkedWorkspaceAsync(userId, ct);
        var session = await _sessions.GetActiveAsync(userId, ct);

        var link = await db.MarketplaceStallAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.IsActive, ct);

        return Ok(ApiResponse.Ok(new
        {
            workspace = workspace is null
                ? null
                : new
                {
                    workspace.WorkspaceId,
                    workspace.WorkspaceType,
                    workspace.Name,
                    stallLocationName = workspace.StallLocation?.Name ?? "",
                    operatorName = link?.OperatorName ?? "",
                    idLast3 = link?.IdLast3 ?? ""
                },
            hasActiveSession = session is not null,
            session = session is null
                ? null
                : MarketplaceOperatorSessionService.ToDto(session, workspace ?? session.Workspace)
        }));
    }
}
