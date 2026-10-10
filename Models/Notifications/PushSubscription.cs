using System.ComponentModel.DataAnnotations;

namespace CDM_OneServe_API.Models.Notifications;

/// <summary>
/// A browser Web Push subscription (PushSubscription sent by
/// PushManager.subscribe()), owned by one signed-in OneServe user.
///
/// The VAPID key pair lives OUTSIDE the API (see the Node notification
/// service). This table only stores what the browser handed us: the
/// push service endpoint plus the two public keys used to encrypt the
/// payload. Nothing here can decrypt a notification - only the browser
/// that created the row can.
/// </summary>
public class PushSubscription
{
    /// <summary>Explicit, because EF's convention only matches "Id" or
    /// "&lt;ClassName&gt;Id".</summary>
    [Key]
    public int PushSubscriptionId { get; set; }


    // =========================================================
    // OWNERSHIP
    // =========================================================
    // Every read/write is filtered by this value; the frontend never
    // chooses it (it comes from the JWT in the controller).
    // =========================================================

    public int UserId { get; set; }


    // =========================================================
    // BROWSER PUSH SUBSCRIPTION
    // =========================================================

    /// <summary>Push service URL (FCM, Mozilla autopush, WNS, ...).</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Browser public key (base64url) used to encrypt payloads.</summary>
    public string P256dh { get; set; } = string.Empty;

    /// <summary>Browser authentication secret (base64url).</summary>
    public string Auth { get; set; } = string.Empty;

    /// <summary>Reporting only - helps a user recognise their devices.</summary>
    public string? UserAgent { get; set; }

    public string? DeviceLabel { get; set; }


    // =========================================================
    // LIFECYCLE
    // =========================================================

    /// <summary>
    /// False once a push service answered 404/410 ("I do not know this
    /// subscription any more"). The row is kept so the browser can be
    /// told to re-subscribe, and so we can report a graceful
    /// "notifications stopped working" state instead of a stack trace.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime? DeactivatedAt { get; set; }

    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
