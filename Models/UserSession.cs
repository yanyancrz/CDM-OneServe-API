namespace CDM_OneServe_API.Models;

/// <summary>
/// One browser/device that signed in to OneServe.
///
/// WHY THIS EXISTS
/// The JWT is stateless on purpose - there is no server-side logout and no
/// session store in the auth path, which is a real security property. The
/// cost is that the server cannot tell whether an account is also signed in
/// somewhere else, so it cannot warn a user about it.
///
/// This table adds exactly that, and nothing else: a device key minted at
/// login, kept next to the token in the browser. The token still does all the
/// authorising; this only ever answers "did somebody else sign in after you?".
///
/// It is NOT an allow-list. A session row being deleted does not invalidate its
/// JWT, and nothing in the auth path reads this table to grant access.
/// </summary>
public class UserSession
{
    public int UserSessionId { get; set; }

    public int UserId { get; set; }

    /// <summary>
    /// Opaque per-login identifier. Sent back by the browser as the
    /// X-Session-Key header so the server knows which row is "this device".
    /// </summary>
    public string SessionKey { get; set; } = string.Empty;

    /// <summary>Short device label derived from the login's User-Agent.</summary>
    public string? DeviceLabel { get; set; }

    /// <summary>Truncated User-Agent, for support questions.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Remote address of the login, when the proxy exposed one.</summary>
    public string? IpAddress { get; set; }

    // UTC, like the column it maps to. DateTime.UtcNow rather than Now so the
    // stored value never depends on the host's timezone setting.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// True once the user has answered "sign out everywhere else". Kept as a
    /// flag rather than deleting the row, so the history stays readable.
    /// </summary>
    public bool IsRevoked { get; set; }
}
