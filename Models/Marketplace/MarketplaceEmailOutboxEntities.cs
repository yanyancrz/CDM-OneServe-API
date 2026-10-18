// ============================================================
// CAMPUSMARKET - email outbox
//
// Created by Database/Marketplace/017_email_outbox.sql.
//
// WHY AN OUTBOX
//   Sending an email inside the request that created the order means a
//   transient SMTP failure loses the receipt forever, and a slow provider
//   makes the buyer wait. The outbox decouples the two: the order is
//   committed, a row is queued, and a worker sends it with retries.
//
//   The CampusMarket original used Resend with an Idempotency-Key of
//   campusmarket-email-{jobId}, so a retry after a timeout never double-sends.
//   This system uses Gmail SMTP (MailKit) through the existing EmailService,
//   which has no idempotency key - so the worker must be careful never to
//   claim the same row twice. The claim logic lives in C#
//   (MarketplaceEmailOutboxService), not in a stored procedure.
//
//   Emails are created ONLY from a confirmed cash payment, a verified online
//   payment, or a confirmed refund. Never from a request that might still fail.
// ============================================================

namespace CDM_OneServe_API.Models.Marketplace;

/// <summary>
/// A queued email: receipt (on payment) or refund (on a confirmed refund).
///
/// One of each per order, enforced by the UNIQUE (OrderId, Kind) key.
/// </summary>
public class MarketplaceEmailOutbox
{
    public long Id { get; set; }

    public int OrderId { get; set; }
    public MarketplaceOrder? Order { get; set; }

    /// <summary>receipt | refund.</summary>
    public string Kind { get; set; } = "receipt";

    /// <summary>How many times the worker has tried.</summary>
    public int Attempts { get; set; }

    /// <summary>When the worker may next try.</summary>
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;

    /// <summary>The worker's lock, or null when free.</summary>
    public DateTime? LeaseUntil { get; set; }

    public string? LeaseId { get; set; }

    /// <summary>When it was sent, or null.</summary>
    public DateTime? SentAt { get; set; }

    public string? ProviderEmailId { get; set; }

    public string? LastError { get; set; }

    public DateTime? FirstAttemptAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
