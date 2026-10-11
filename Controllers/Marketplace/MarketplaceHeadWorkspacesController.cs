using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// Head-only management of the stall world. The Marketplace Head is the
/// module owner (Role 'MarketplaceAdmin'), mirroring how Library and
/// Guidance heads manage their modules.
///
///   Stall locations : GET/POST /api/marketplace/head/stall-locations (+ PUT /{id})
///   Workspaces      : GET /api/marketplace/head/workspaces, POST /save
///   Stall accounts  : POST /api/marketplace/head/workspaces/{id}/account (+ DELETE, + password reset)
///   Audit trail     : GET /api/marketplace/head/audit
///
/// Creating a stall only creates the workspace record. People never get
/// individual logins here - a workspace gets at most ONE login account, and
/// humans are identified per shift by the operator name they enter.
/// </summary>
[ApiController]
[Route("api/marketplace/head")]
[Authorize]
public class MarketplaceHeadWorkspacesController : MarketplaceControllerBase
{
    private readonly MarketplaceWorkspaceService _workspaces;

    public MarketplaceHeadWorkspacesController(MarketplaceWorkspaceService workspaces)
    {
        _workspaces = workspaces;
    }

    // ==========================================================
    // stall locations (FoodHub groupings)
    // ==========================================================

    [HttpGet("stall-locations")]
    public async Task<IActionResult> StallLocations(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _workspaces.ListLocationsAsync(userId, ct));
    }

    [HttpPost("stall-locations")]
    public async Task<IActionResult> CreateStallLocation(
        [FromBody] SaveStallLocationRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _workspaces.SaveLocationAsync(userId, null, request, ct));
    }

    [HttpPut("stall-locations/{id:int}")]
    public async Task<IActionResult> UpdateStallLocation(
        int id,
        [FromBody] SaveStallLocationRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _workspaces.SaveLocationAsync(userId, id, request, ct));
    }

    // ==========================================================
    // workspaces (stalls + BusinessHub)
    // ==========================================================

    [HttpGet("workspaces")]
    public async Task<IActionResult> Workspaces(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _workspaces.ListWorkspacesAsync(userId, ct));
    }

    [HttpPost("workspaces/save")]
    public async Task<IActionResult> SaveWorkspace(
        [FromBody] SaveWorkspaceRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _workspaces.SaveWorkspaceAsync(userId, request, ct));
    }

    // ==========================================================
    // one login account per workspace
    // ==========================================================

    [HttpPost("workspaces/{id:int}/account")]
    public async Task<IActionResult> CreateStallAccount(
        int id,
        [FromBody] CreateStallAccountRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _workspaces.CreateStallAccountAsync(userId, id, request, ct));
    }

    /// <summary>
    /// Names (or renames) the designated operator of a stall login: the name
    /// plus the last 3 ID digits shown on the setup screen with a reminder.
    /// </summary>
    [HttpPut("workspaces/{id:int}/account/operator")]
    public async Task<IActionResult> SetStallOperator(
        int id,
        [FromBody] SetStallOperatorRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _workspaces.SetStallOperatorAsync(userId, id, request, ct));
    }

    [HttpDelete("workspaces/{id:int}/account")]
    public async Task<IActionResult> DeactivateStallAccount(int id, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _workspaces.DeactivateStallAccountAsync(userId, id, ct));
    }

    [HttpPost("workspaces/{id:int}/account/password")]
    public async Task<IActionResult> ResetStallAccountPassword(
        int id,
        [FromBody] ResetStallAccountPasswordRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _workspaces.ResetStallAccountPasswordAsync(userId, id, request, ct));
    }

    // ==========================================================
    // audit trail
    // ==========================================================

    [HttpGet("audit")]
    public async Task<IActionResult> Audit(
        [FromQuery] int? workspaceId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? operatorName,
        [FromQuery] string? actionType,
        [FromQuery] int? productId,
        [FromQuery] int? orderId,
        [FromQuery] int take = 200,
        CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _workspaces.ListAuditAsync(
            userId, workspaceId, from, to, operatorName, actionType,
            productId, orderId, take, ct));
    }
}
