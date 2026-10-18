using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// Seller onboarding: a verified Student or Faculty account applies to sell, and
/// the Marketplace Head reviews the applications.
///
/// Created by Database/Marketplace/012_seller_applications.sql.
///
/// [Authorize] is the plain OneServe JWT; the caller is always read from the
/// token, never from a body field.
/// </summary>
[ApiController]
[Route("api/marketplace/seller-applications")]
[Authorize]
public class MarketplaceSellerApplicationController : MarketplaceControllerBase
{
    private readonly MarketplaceSellerApplicationService _applications;

    public MarketplaceSellerApplicationController(MarketplaceSellerApplicationService applications) =>
        _applications = applications;

    // =====================================================
    // BUYER - apply
    // =====================================================

    /// <summary>
    /// Files an application to become a seller. Refuses with a 409 when one is
    /// already pending, so a double-tap cannot produce two.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Apply(
        [FromBody] ApplyToSellRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _applications.ApplyAsync(
            userId, request.BusinessName, request.Description, request.DocumentPath, ct));
    }

    /// <summary>Every application the signed-in account has filed.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _applications.GetMineAsync(userId, ct));
    }

    // =====================================================
    // HEAD - the review queue
    // =====================================================

    /// <summary>
    /// The Head's review queue. Pending first, oldest first. Filter by status.
    /// </summary>
    [HttpGet("head")]
    public async Task<IActionResult> Queue(
        [FromQuery] string? status,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var headId)) return InvalidSession();

        return ToAction(await _applications.ListAsync(headId, status, ct));
    }

    /// <summary>
    /// The Head's decision. Approving records that the applicant may sell; the
    /// Head then creates the storefront and assigns them as owner through
    /// /api/marketplace/stores. That keeps governance in one place.
    /// </summary>
    [HttpPost("head/{applicationId:int}/review")]
    public async Task<IActionResult> Review(
        int applicationId,
        [FromBody] ReviewSellerApplicationRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var headId)) return InvalidSession();

        return ToAction(await _applications.ReviewAsync(
            headId, applicationId, request.Approve, request.Note, ct));
    }
}
