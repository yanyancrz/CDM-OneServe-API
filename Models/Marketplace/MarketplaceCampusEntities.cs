// ============================================================
// CAMPUSMARKET - campus services entities
//
// Created by Database/Marketplace/003_campus_services.sql. These are the four
// campus services the original marketplace did not have:
//
//   * pre-ordering + order claim codes  (on MarketplaceOrder, 003)
//   * the Paper Shop / Supplies category (no table - it is a Category value)
//   * the Student / Faculty ID claim     (MarketplaceIdClaim)
//   * the campus card and its ledger     (MarketplaceCampusCard, MarketplaceCardTransaction)
//
// ARCHITECTURAL RULE (unchanged)
//   There is no marketplace users table: every table that needs a person
//   points at the EXISTING `users`.`Id`. Student and Faculty accounts, and
//   authentication, stay owned by OneServe.
//
// Money is integer *_Centavos here too, matching marketplace_orders.
//
// The CHECK constraints (a card balance never negative, a ledger amount always
// positive) live in the SQL and are enforced by the database, not only by the
// service layer.
// ============================================================

namespace CDM_OneServe_API.Models.Marketplace;

/// <summary>Status of a campus card.</summary>
public static class CampusCardStatus
{
    /// <summary>Can be used to pay. A blocked card keeps its balance.</summary>
    public const string Active = "Active";

    /// <summary>Reported lost or stolen. Keeps the balance, cannot pay.</summary>
    public const string Blocked = "Blocked";

    public static readonly string[] All = { Active, Blocked };

    public static bool IsValid(string? status) =>
        status is not null && All.Contains(status, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? status) =>
        string.Equals(status, Blocked, StringComparison.OrdinalIgnoreCase) ? Blocked : Active;
}

/// <summary>One kind of movement on a campus card ledger.</summary>
public static class CampusCardTransactionType
{
    /// <summary>Staff loaded the card at the counter.</summary>
    public const string Credit = "Credit";

    /// <summary>An order was paid with the card balance.</summary>
    public const string Debit = "Debit";

    /// <summary>A cancelled or refunded order returned the balance.</summary>
    public const string Refund = "Refund";

    public static readonly string[] All = { Credit, Debit, Refund };

    public static bool IsValid(string? type) =>
        type is not null && All.Contains(type, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? type) => type switch
    {
        Credit => Credit,
        Debit => Debit,
        _ => Refund
    };
}

/// <summary>Why somebody is requesting a campus ID.</summary>
public static class IdClaimType
{
    /// <summary>Never received one.</summary>
    public const string FirstIssue = "FirstIssue";

    /// <summary>Worn out or the print is gone.</summary>
    public const string Replacement = "Replacement";

    public const string Lost = "Lost";
    public const string Damaged = "Damaged";

    public static readonly string[] All = { FirstIssue, Replacement, Lost, Damaged };

    public static bool IsValid(string? type) =>
        type is not null && All.Contains(type, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? type) =>
        All.FirstOrDefault(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase))
        ?? Replacement;

    /// <summary>Lost and Damaged need a reason; the others do not.</summary>
    public static bool RequiresReason(string? type) =>
        Normalize(type) is Lost or Damaged;
}

/// <summary>Where a campus ID claim has got to.</summary>
public static class IdClaimStatus
{
    /// <summary>Waiting for staff to look at it.</summary>
    public const string Pending = "Pending";

    /// <summary>Approved. The person can collect it with their claim code.</summary>
    public const string ReadyForClaim = "ReadyForClaim";

    /// <summary>Handed over.</summary>
    public const string Claimed = "Claimed";

    /// <summary>Turned down. ReviewNotes says why.</summary>
    public const string Rejected = "Rejected";

    public static readonly string[] All = { Pending, ReadyForClaim, Claimed, Rejected };

    /// <summary>Statuses that still block the person from opening another claim.</summary>
    public static readonly string[] Open = { Pending, ReadyForClaim };

    public static bool IsOpen(string? status) =>
        Open.Contains(status ?? "", StringComparer.OrdinalIgnoreCase);

    public static bool IsValid(string? status) =>
        status is not null && All.Contains(status, StringComparer.OrdinalIgnoreCase);

    /// <summary>Legal moves. A claim cannot go back to Pending once released.</summary>
    public static string? Next(string? current, bool approve) => (current ?? "") switch
    {
        Pending => approve ? ReadyForClaim : Rejected,
        ReadyForClaim => Claimed,
        // Claimed and Rejected are final.
        _ => null
    };

    public static bool IsAllowedNext(string? current, bool approve) =>
        Next(current, approve) is not null;
}

/// <summary>
/// A student's or faculty member's campus card. One per user, enforced by a
/// UNIQUE index on UserId.
/// </summary>
public class MarketplaceCampusCard
{
    public int CardId { get; set; }

    /// <summary>The owner. The EXISTING OneServe user - never a copy.</summary>
    public int UserId { get; set; }

    public User? User { get; set; }

    /// <summary>The number printed on the plastic, e.g. CDM-000123.</summary>
    public string CardNumber { get; set; } = "";

    /// <summary>Never negative - the SQL has a CHECK constraint as well.</summary>
    public int BalanceCentavos { get; set; }

    /// <summary>Active | Blocked.</summary>
    public string Status { get; set; } = CampusCardStatus.Active;

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoadedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public List<MarketplaceCardTransaction> Transactions { get; set; } = new();
}

/// <summary>
/// One row on a campus card's ledger. Append-only - nothing edits or deletes a
/// row, so a disputed balance can always be reconstructed by replaying it.
/// </summary>
public class MarketplaceCardTransaction
{
    public long TransactionId { get; set; }

    public int CardId { get; set; }
    public MarketplaceCampusCard? Card { get; set; }

    /// <summary>Denormalised from the card so history survives a card reissue.</summary>
    public int UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Credit | Debit | Refund.</summary>
    public string Type { get; set; } = CampusCardTransactionType.Credit;

    /// <summary>Always positive. The direction comes from <see cref="Type"/>.</summary>
    public int AmountCentavos { get; set; }

    /// <summary>Balance right after this row.</summary>
    public int BalanceAfterCentavos { get; set; }

    /// <summary>Set when this movement came from an order. Null once it is deleted.</summary>
    public int? ReferenceOrderId { get; set; }

    public MarketplaceOrder? Order { get; set; }

    /// <summary>Staff who did it at the counter. Null for order-driven debits.</summary>
    public int? PerformedByUserId { get; set; }

    public User? PerformedByUser { get; set; }

    /// <summary>
    /// Duty-shift operator name, taken from the server-validated operator
    /// session - never from the request body.
    /// </summary>
    public string? OperatorName { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A request for a Student or Faculty campus ID: first issue, replacement, lost
/// or damaged.
///
/// A UNIQUE generated column (OpenUserId) allows only one open claim per user,
/// so two impatient double-taps cannot produce two replacement IDs.
/// </summary>
public class MarketplaceIdClaim
{
    public int IdClaimId { get; set; }

    /// <summary>The claimant. The EXISTING OneServe user.</summary>
    public int UserId { get; set; }

    public User? User { get; set; }

    /// <summary>FirstIssue | Replacement | Lost | Damaged.</summary>
    public string ClaimType { get; set; } = IdClaimType.Replacement;

    public string Reason { get; set; } = "";

    /// <summary>Pending | ReadyForClaim | Claimed | Rejected.</summary>
    public string Status { get; set; } = IdClaimStatus.Pending;

    /// <summary>
    /// The short code the person shows to collect the ID. Unique; null until
    /// staff release the claim.
    /// </summary>
    public string? ClaimCode { get; set; }

    /// <summary>Staff who reviewed it. Null once that user is deleted.</summary>
    public int? ReviewedByUserId { get; set; }

    public User? ReviewedByUser { get; set; }

    /// <summary>
    /// The database-computed "who currently has an open claim" column. UNIQUE
    /// on it is what stops two open claims for one user. Read-only: the value
    /// comes from the generated column in 003_campus_services.sql, and EF must
    /// never write it.
    /// </summary>
    public int? OpenUserId { get; set; }

    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNotes { get; set; }
    public DateTime? ClaimedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
