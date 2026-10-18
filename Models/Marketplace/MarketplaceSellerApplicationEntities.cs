// ============================================================
// CAMPUSMARKET - seller applications
//
// Created by Database/Marketplace/012_seller_applications.sql.
//
// WHY THIS IS NOT THE ZIP'S FLOW
//   CampusMarket grants a `seller` role on approval and the seller then creates
//   their own store. This system has ONE Role per user, so a seller cannot be a
//   Student at the same time. Store creation is also a Head act here, not a
//   seller act, because who sells where is a governance decision.
//
//   So the mapping is:
//     apply   -> "I would like to sell"
//     approve -> "you may, and I will give you a storefront"
//     reject  -> "not now, with a reason"
//   The Head then creates the storefront and assigns the applicant as owner via
//   the existing /stores endpoint. The application is the intent signal; the
//   storefront is the act.
//
// IDENTITY GATE
//   An applicant must be a verified Student or Faculty account
//   (AccountStatus = 'Active', which is only set on physical ID approval by
//   /api/admin/registration-verification). The service refuses anything else,
//   so an unverified or non-buyer account cannot apply to sell.
// ============================================================

namespace CDM_OneServe_API.Models.Marketplace;

/// <summary>Status of a seller application.</summary>
public static class MarketplaceSellerApplicationStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    public static readonly string[] All = { Pending, Approved, Rejected };

    public static string Normalize(string? status) =>
        All.FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase))
        ?? Pending;
}

/// <summary>
/// A request to become a seller.
///
/// One pending application per user, enforced by a generated column so two
/// impatient double-taps cannot produce two applications.
/// </summary>
public class MarketplaceSellerApplication
{
    public int ApplicationId { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public string BusinessName { get; set; } = "";

    public string Description { get; set; } = "";

    public string? DocumentPath { get; set; }

    /// <summary>pending | approved | rejected.</summary>
    public string Status { get; set; } = MarketplaceSellerApplicationStatus.Pending;

    public string? ReviewNote { get; set; }

    public int? ReviewedBy { get; set; }
    public User? ReviewedByUser { get; set; }

    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Database-computed marker for "this person has one pending application".
    /// Read-only: it comes from the generated column in 012.
    /// </summary>
    public int? PendingUserId { get; set; }
}
