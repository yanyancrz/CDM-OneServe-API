// ============================================================
// CAMPUSMARKET - reviews
//
// Created by Database/Marketplace/013_reviews.sql.
//
// A buyer reviews the STORE they bought from, after a completed order. One
// review per order ever; tied to the order, not to a product line, because a
// store is what gets rated, not a single item.
//
// NO MODERATION
//   There is no review reply, no seller response, no hide/flag/delete-review
//   command and no admin review queue. A review can only disappear when its
//   author's account is purged (body and photos blanked). A buyer cannot edit
//   or delete their own review. These are the CampusMarket rules, ported as-is.
// ============================================================

namespace CDM_OneServe_API.Models.Marketplace;

/// <summary>
/// A buyer's review of a storefront, tied to one completed order.
///
/// One review per order ever: the UNIQUE key on OrderId enforces that, and the
/// service refuses a second one with a 409. Tied to the order rather than to a
/// product line, because the store is what gets rated.
/// </summary>
public class MarketplaceReview
{
    public int ReviewId { get; set; }

    /// <summary>
    /// The order this review is about. SetNull on delete: a review should outlive
    /// the order it refers to, so the rating and the comment stay visible to
    /// future buyers. The review is still about that order, just without the link.
    /// </summary>
    public int? OrderId { get; set; }

    public MarketplaceOrder? Order { get; set; }

    /// <summary>
    /// The author. SetNull on delete: the review outlives the account, so the
    /// rating and the comment stay useful. The author's name is not stored on the
    /// row, so an anonymised review is still useful.
    /// </summary>
    public int? UserId { get; set; }

    public User? User { get; set; }

    /// <summary>The store being rated. Cascade: a deleted store takes its reviews.</summary>
    public int StoreId { get; set; }

    public MarketplaceStore? Store { get; set; }

    /// <summary>1-5. The CHECK constraint is the second line of defence.</summary>
    public int Rating { get; set; }

    public string Body { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<MarketplaceReviewPhoto> Photos { get; set; } = new();
}

/// <summary>
/// One gallery image for a review.
///
/// Up to five per review, HTTPS only - the same rules as product photos. The
/// review's body is the words; the photos are the evidence.
/// </summary>
public class MarketplaceReviewPhoto
{
    public int PhotoId { get; set; }

    public int ReviewId { get; set; }
    public MarketplaceReview? Review { get; set; }

    public string Url { get; set; } = "";

    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
