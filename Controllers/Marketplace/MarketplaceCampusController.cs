using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// Campus services: the canteen/paper shop pre-order window and order claim
/// codes. Created by Database/Marketplace/003_campus_services.sql.
///
/// [Authorize] is the plain OneServe JWT - the caller is read from the token,
/// never from a body field, and Student/Faculty accounts stay owned by OneServe.
/// The real role check lives in MarketplaceAccessService: a signed-in Admin
/// token passes [Authorize] and is still refused with a 403 by the service.
/// </summary>
[ApiController]
[Route("api/marketplace/campus")]
[Authorize]
public class MarketplaceCampusController : MarketplaceControllerBase
{
    private readonly MarketplaceClaimService _claims;

    public MarketplaceCampusController(MarketplaceClaimService claims) => _claims = claims;

    /// <summary>
    /// The bookable pickup-slot window. The buyer screen renders from this
    /// rather than hardcoding how far ahead a slot may be taken, so changing the
    /// rule later needs no app release.
    /// </summary>
    [HttpGet("preorder-options")]
    public async Task<IActionResult> PreOrderOptions(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _claims.GetOptionsAsync(userId, ct));
    }

    /// <summary>
    /// The claim code for one of the caller's own orders, issued on demand if
    /// the order does not have one yet.
    ///
    /// Scoped to the caller inside the service, so another buyer's order id is
    /// a 404 rather than a way to read their code.
    /// </summary>
    [HttpGet("orders/{orderId:int}/claim-code")]
    public async Task<IActionResult> ClaimCode(int orderId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _claims.EnsureClaimCodeAsync(userId, orderId, ct));
    }
}
