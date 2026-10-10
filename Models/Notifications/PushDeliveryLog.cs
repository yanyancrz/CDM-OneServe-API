using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.Models.Notifications;

/// <summary>
/// Audit trail for one delivery attempt. This is what lets us prove a
/// notification was sent, see why one failed, and safely delete
/// subscriptions a push service no longer knows about (404/410).
/// </summary>
public class PushDeliveryLog
{
    [Key]
    public long PushDeliveryLogId { get; set; }

    public long PushOutboxId { get; set; }

    public int PushSubscriptionId { get; set; }

    /// <summary>Sent | Failed | Expired | Skipped.</summary>
    public string Status { get; set; } = "Sent";

    /// <summary>HTTP status returned by the push service.</summary>
    public int? StatusCode { get; set; }

    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
