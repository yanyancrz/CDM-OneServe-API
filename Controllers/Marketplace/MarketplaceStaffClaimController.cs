using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// Claim verification at the counter: looking up an order's claim code and
/// releasing it. Created by Database/Marketplace/003_campus_services.sql.
///
/// This is the screen that turns "Ready for pickup" into an item actually
/// handed over, and it records who released it.
///
/// Releasing requires a live operator session (X-Operator-Session) and refuses
/// when the session's stall is not the order's stall - a canteen terminal must
/// not release a paper shop pickup.
/// </summary>
[ApiController]
[Route("api/marketplace/staff/claims")]
[Authorize]
public class MarketplaceStaffClaimController : MarketplaceControllerBase
{
    private readonly MarketplaceClaimService _claims;

    public MarketplaceStaffClaimController(MarketplaceClaimService claims) => _claims = claims;

    /// <summary>
    /// Looks a claim code up without changing anything, so staff can confirm the
    /// code belongs to the person in front of them before releasing it.
    ///
    /// A code that is valid but belongs to a cancelled or already-collected
    /// order comes back 200 with Found=false and the reason - staff need to be
    /// able to say "that was cancelled" as confidently as "no such code".
    /// </summary>
    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery] string? claimCode, CancellationToken ct)
    {
        if (!TryGetUserId(out var staffId)) return InvalidSession();

        return ToAction(await _claims.LookupAsync(staffId, claimCode ?? "", ct));
    }

    /// <summary>
    /// Releases a claimed order: marks it collected, completes it and consumes
    /// the reserved stock. The counter's "hand it over" button.
    /// </summary>
    [HttpPost("verify")]
    public async Task<IActionResult> Verify(
        [FromBody] VerifyClaimRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var staffId)) return InvalidSession();

        return ToAction(await _claims.VerifyAsync(
            staffId, request.ClaimCode, request.Notes, OperatorSession(), ct));
    }

    /// <summary>
    /// Issues a fresh claim code for an order, for when the buyer's screen could
    /// not show one.
    /// </summary>
    [HttpPost("orders/{orderId:int}/reissue")]
    public async Task<IActionResult> Reissue(int orderId, CancellationToken ct)
    {
        if (!TryGetUserId(out var staffId)) return InvalidSession();

        return ToAction(await _claims.ReissueAsync(staffId, orderId, OperatorSession(), ct));
    }
}
