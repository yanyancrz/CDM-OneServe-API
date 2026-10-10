namespace CDM_OneServe_API.DTOs.Notifications;

/// <summary>
/// Payload POSTed by the browser. Everything except <see cref="Endpoint"/>
/// is optional because some browsers omit the optional fields.
/// </summary>
public class PushSubscriptionDto
{
    public string Endpoint { get; set; } = string.Empty;

    public string P256dh { get; set; } = string.Empty;

    public string Auth { get; set; } = string.Empty;

    /// <summary>Optional label so a user can tell their devices apart.</summary>
    public string? DeviceLabel { get; set; }
}

/// <summary>
/// Sent back to the browser so the UI can show "notifications on/off"
/// without asking the browser for a subscription it already has.
/// </summary>
public class PushSubscriptionStatusDto
{
    public int PushSubscriptionId { get; set; }

    public string Endpoint { get; set; } = string.Empty;

    public string? DeviceLabel { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime LastUsedAt { get; set; }
}

public class PushUnsubscribeDto
{
    /// <summary>Either the endpoint, or the stored subscription id.</summary>
    public string? Endpoint { get; set; }

    public int? PushSubscriptionId { get; set; }
}

/// <summary>Shape of the payload the service worker puts on the notification.</summary>
public class PushOutboxDto
{
    public long PushOutboxId { get; set; }

    public int UserId { get; set; }

    public int? PushSubscriptionId { get; set; }

    public string Module { get; set; } = "General";

    public string Type { get; set; } = "General";

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string? IconUrl { get; set; }

    public string? BadgeUrl { get; set; }

    public string TargetUrl { get; set; } = "/dashboard";

    public string? Tag { get; set; }

    public byte Priority { get; set; }

    public string Status { get; set; } = "Pending";

    public byte Attempts { get; set; }

    public DateTime ScheduledAt { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>Result of a web-push delivery attempt, written by the Node service.</summary>
public class PushDeliveryResultDto
{
    public long PushOutboxId { get; set; }

    public int PushSubscriptionId { get; set; }

    /// <summary>Sent | Failed | Expired</summary>
    public string Status { get; set; } = "Sent";

    public int? StatusCode { get; set; }

    public string? Error { get; set; }

    /// <summary>True when the push service no longer knows the subscription.</summary>
    public bool SubscriptionIsGone { get; set; }
}
