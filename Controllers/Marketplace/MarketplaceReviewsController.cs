using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// Reviews: a buyer rates the store they bought from, after a completed order.
///
/// Created by Database/Marketplace/013_reviews.sql.
///
/// [Authorize] is the plain OneServe JWT; the caller is always read from the
/// token, never from a body field.
/// </summary>
[ApiController]
[Route("api/marketplace/reviews")]
[Authorize]
public class MarketplaceReviewsController : MarketplaceControllerBase
{
    private readonly MarketplaceReviewService _reviews;

    public MarketplaceReviewsController(MarketplaceReviewService reviews) => _reviews = reviews;

    /// <summary>
    /// Posts a review for a completed, paid order. One per order ever.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Review(
        [FromBody] ReviewOrderRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _reviews.ReviewAsync(
            userId, request.OrderId, request.Rating, request.Body, request.Photos, ct));
    }

    /// <summary>
    /// A store's reviews, newest first, capped at 100.
    /// </summary>
    [HttpGet("stores/{storeId:int}")]
    public async Task<IActionResult> ForStore(int storeId, CancellationToken ct) =>
        ToAction(await _reviews.ListForStoreAsync(storeId, ct));

    /// <summary>
    /// A store's rating summary: average rating (one decimal) and count.
    /// A store with no reviews has no rating, not a zero.
    /// </summary>
    [HttpGet("stores/{storeId:int}/rating")]
    public async Task<IActionResult> Rating(int storeId, CancellationToken ct) =>
        ToAction(await _reviews.RatingForStoreAsync(storeId, ct));
}
