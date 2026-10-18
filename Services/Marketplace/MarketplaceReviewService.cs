using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Marketplace;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Reviews: a buyer rates the store they bought from, after a completed order.
///
/// Created by Database/Marketplace/013_reviews.sql.
///
/// RULES (from the CampusMarket spec)
///   * buyers only, completed orders only;
///   * one review per order ever (UNIQUE on OrderId, enforced twice);
///   * tied to the order, not to a product line - the store is what gets rated;
///   * rating 1-5, body up to 2000, up to 5 photos, HTTPS only;
///   * no moderation, no reply, no edit, no delete by the author.
///
/// STORE RATING
///   A store's rating is the average of its reviews, rounded to one decimal.
///   It is computed on read rather than stored, so it can never drift. A store
///   with no reviews has no rating, not a zero.
/// </summary>
public class MarketplaceReviewService
{
    private readonly AppDbContext _db;
    private readonly MarketplaceStoreAccessService _access;

    /// <summary>How many reviews one page returns.</summary>
    private const int PageSize = 100;

    /// <summary>The gallery limit per review, from the CampusMarket spec.</summary>
    public const int MaxReviewPhotos = 5;

    public MarketplaceReviewService(AppDbContext db, MarketplaceStoreAccessService access)
    {
        _db = db;
        _access = access;
    }

    // =====================================================
    // BUYER - write a review
    // =====================================================

    /// <summary>
    /// Reviews a completed order. Refuses with a 409 when the order already has
    /// a review, so a retry or a double tap cannot create a second one.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceReviewDto>> ReviewAsync(
        int userId, int orderId, int rating, string? body, List<string>? photoUrls, CancellationToken ct = default)
    {
        if (rating is < 1 or > 5)
            return MarketplaceResult<MarketplaceReviewDto>.BadRequest("Rating must be between 1 and 5.");

        var order = await _db.MarketplaceOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderId == orderId && o.UserId == userId, ct);

        if (order is null)
            return MarketplaceResult<MarketplaceReviewDto>.NotFound("That order does not exist.");

        if (order.Status != MarketplaceOrderStatus.Completed)
            return MarketplaceResult<MarketplaceReviewDto>.BadRequest(
                "You can only review an order once it is completed.");

        if (order.PaymentStatus != "paid")
            return MarketplaceResult<MarketplaceReviewDto>.BadRequest(
                "You can only review an order that has been paid.");

        var store = await _db.MarketplaceStores.AsNoTracking()
            .FirstOrDefaultAsync(s => s.StoreId == order.WorkspaceId == null
                ? false
                : s.WorkspaceId == order.WorkspaceId, ct);

        // Resolve the store through the workspace, because an order's StoreId is
        // not stored on the order - it is derived from the workspace it was
        // fulfilled from. A store without a workspace means the buyer can still
        // review the workspace's store, which is the only store it could be.
        var storeId = store?.StoreId ?? 0;
        if (storeId == 0)
            return MarketplaceResult<MarketplaceReviewDto>.BadRequest(
                "That order cannot be reviewed - it has no storefront.");

        var existing = await _db.MarketplaceReviews.AsNoTracking()
            .AnyAsync(r => r.OrderId == orderId, ct);

        if (existing)
            return MarketplaceResult<MarketplaceReviewDto>.Conflict(
                "You already reviewed this order.");

        var review = new MarketplaceReview
        {
            OrderId = orderId,
            UserId = userId,
            StoreId = storeId,
            Rating = rating,
            Body = (body ?? "").Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _db.MarketplaceReviews.Add(review);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The UNIQUE OrderId index fired: a concurrent double-tap won.
            _db.Entry(review).State = EntityState.Detached;
            return MarketplaceResult<MarketplaceReviewDto>.Conflict(
                "You already reviewed this order.");
        }

        // Photos are added after the review exists, so they can never reference a
        // review that was not actually saved.
        if (photoUrls is { Count: > 0 })
        {
            var sort = 0;
            foreach (var raw in photoUrls.Take(MaxReviewPhotos))
            {
                var url = (raw ?? "").Trim();
                if (url.Length == 0) continue;
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || uri.Scheme != Uri.UriSchemeHttps)
                    continue;

                _db.MarketplaceReviewPhotos.Add(new MarketplaceReviewPhoto
                {
                    ReviewId = review.ReviewId,
                    Url = url,
                    SortOrder = sort++,
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (sort > 0) await _db.SaveChangesAsync(ct);
        }

        var saved = await _db.MarketplaceReviews.AsNoTracking()
            .Include(r => r.Photos)
            .Include(r => r.User)
            .FirstAsync(r => r.ReviewId == review.ReviewId, ct);

        return MarketplaceResult<MarketplaceReviewDto>.Created(
            ToDto(saved), "Review posted. Thank you.");
    }

    // =====================================================
    // BUYER + STOREFRONT - read
    // =====================================================

    /// <summary>
    /// A store's reviews, newest first, capped at 100. The same cap the
    /// CampusMarket spec uses.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceReviewDto>>> ListForStoreAsync(
        int storeId, CancellationToken ct = default)
    {
        var reviews = await _db.MarketplaceReviews.AsNoTracking()
            .Include(r => r.Photos)
            .Include(r => r.User)
            .Where(r => r.StoreId == storeId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(PageSize)
            .ToListAsync(ct);

        return MarketplaceResult<List<MarketplaceReviewDto>>.Ok(
            reviews.Select(ToDto).ToList());
    }

    /// <summary>
    /// A store's rating summary: average rating (rounded to one decimal) and
    /// review count. A store with no reviews has no rating, not a zero - so the
    /// DTO carries null and the UI can say "Be the first to review".
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceStoreRatingDto>> RatingForStoreAsync(
        int storeId, CancellationToken ct = default)
    {
        var rows = await _db.MarketplaceReviews.AsNoTracking()
            .Where(r => r.StoreId == storeId)
            .Select(r => r.Rating)
            .ToListAsync(ct);

        if (rows.Count == 0)
            return MarketplaceResult<MarketplaceStoreRatingDto>.Ok(
                new MarketplaceStoreRatingDto { StoreId = storeId, ReviewCount = 0, Rating = null });

        var average = rows.Average(r => (double)r);

        return MarketplaceResult<MarketplaceStoreRatingDto>.Ok(
            new MarketplaceStoreRatingDto
            {
                StoreId = storeId,
                ReviewCount = rows.Count,
                Rating = Math.Round(average, 1)
            });
    }

    // =====================================================
    // INTERNAL
    // =====================================================

    private static MarketplaceReviewDto ToDto(MarketplaceReview r) => new()
    {
        ReviewId = r.ReviewId,
        OrderId = r.OrderId,
        UserId = r.UserId,
        StoreId = r.StoreId,
        Rating = r.Rating,
        Body = r.Body,
        Photos = r.Photos
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.PhotoId)
            .Select(p => new ReviewPhotoDto
            {
                PhotoId = p.PhotoId,
                ReviewId = p.ReviewId,
                Url = p.Url,
                SortOrder = p.SortOrder
            })
            .ToList(),
        CreatedAt = r.CreatedAt
    };

    private const string StaffOnly = "Marketplace Staff access required.";
}
