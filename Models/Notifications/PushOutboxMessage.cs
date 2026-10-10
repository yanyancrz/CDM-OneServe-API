using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.Models.Notifications;

/// <summary>
/// One pending browser notification, written by the API in the SAME
/// transaction as the business event that caused it, and delivered
/// later by the Node notification service.
///
/// The outbox is what makes push reliable: the API never calls a push
/// service on the request thread, so a slow or unreachable push service
/// can never make "reserve a book" fail. Delivery, retry back-off and
/// 404/410 cleanup live in the Node service.
/// </summary>
public class PushOutboxMessage
{
    /// <summary>Explicit [Key]: EF's convention would look for
    /// "PushOutboxMessageId" and silently treat this as a keyless entity,
    /// which then blows up at runtime.</summary>
    [Key]
    public long PushOutboxId { get; set; }

    /// <summary>Recipient. Always the authenticated owner, never a body field.</summary>
    public int UserId { get; set; }

    /// <summary>
    /// When set, only that subscription receives it. NULL means "every
    /// active subscription of the user" (a real browser push).
    /// </summary>
    public int? PushSubscriptionId { get; set; }

    /// <summary>Module tag: LostFound, Library, Guidance, Marketplace, Account.</summary>
    public string Module { get; set; } = "General";

    /// <summary>Event tag, e.g. "ClaimApproved" or "reservation-ready".</summary>
    public string Type { get; set; } = "General";

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string? IconUrl { get; set; }

    public string? BadgeUrl { get; set; }

    /// <summary>Relative path the service worker opens when clicked.</summary>
    public string TargetUrl { get; set; } = "/dashboard";

    /// <summary>Groups/replaces notifications for one subject in the OS tray.</summary>
    public string? Tag { get; set; }

    /// <summary>
    /// Deterministic key that makes the same business event impossible to
    /// notify twice. API appends the UTC date, so one event = one push
    /// per day. Backed by a UNIQUE index, so a race cannot duplicate.
    /// </summary>
    public string DedupeKey { get; set; } = string.Empty;

    /// <summary>1 = must deliver now, 9 = background/reminder.</summary>
    public byte Priority { get; set; } = 5;

    /// <summary>Pending | Processing | Sent | Failed | Cancelled.</summary>
    public string Status { get; set; } = "Pending";

    public byte Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTime ScheduledAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }
}
