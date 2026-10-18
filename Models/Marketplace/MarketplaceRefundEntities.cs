// ============================================================
// CAMPUSMARKET - refunds and returns
//
// Created by Database/Marketplace/014_refunds.sql.
//
// A buyer requests a return/refund on a paid order; the store owner or an
// administrator reviews it; a cash refund is confirmed at the counter.
//
// NO ONLINE PAYMENTS in this system (by decision). The CampusMarket original
// also had a PayMongo refund path (prepare_online_refund / attach_refund /
// confirm_online_refund / reconcileRefund), which is NOT ported here. Only the
// cash path is implemented, and it is gated on the `cash` permission of the
// store that owns the order's workspace.
//
// WHY ONE REQUEST PER ORDER
//   `order_id` is the PRIMARY KEY, so there can be exactly one refund request
//   per order ever. The CampusMarket spec uses the same shape. A second request
//   for the same order is a 409, not a second row.
//
// RESTOCK
//   The request carries a `restock` flag: when approved and confirmed, the
//   reserved stock is released back to the shelf. It is reachable from the UI
//   here (the CampusMarket original hardcoded restock=false and never exposed
//   it). Defaults to true, because a returned item usually goes back on the
//   shelf.
// ============================================================

namespace CDM_OneServe_API.Models.Marketplace;

/// <summary>Status of a refund request.</summary>
public static class MarketplaceRefundStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Processing = "processing";
    public const string Completed = "completed";

    public static readonly string[] All = { Pending, Approved, Rejected, Processing, Completed };

    public static string Normalize(string? status) =>
        All.FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase))
        ?? Pending;
}

/// <summary>
/// A request to return a paid order and get the money back.
///
/// `OrderId` is the primary key, so there is exactly one request per order ever.
/// </summary>
public class MarketplaceRefundRequest
{
    public int OrderId { get; set; }
    public MarketplaceOrder? Order { get; set; }

    public string Reason { get; set; } = "";

    /// <summary>pending | approved | rejected | processing | completed.</summary>
    public string Status { get; set; } = MarketplaceRefundStatus.Pending;

    /// <summary>When approved and confirmed, the stock goes back on the shelf.</summary>
    public bool Restock { get; set; } = true;

    public string? ReviewNote { get; set; }

    public int? ReviewedBy { get; set; }
    public User? ReviewedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
