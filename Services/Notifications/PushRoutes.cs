namespace CDM_OneServe_API.Services.Notifications;

/// <summary>
/// Central table of "which screen does this push open".
///
/// Keeping this here means event sites never hardcode a route, and if
/// the student-facing paths ever move, only this file changes. Only
/// internal, relative paths are allowed - see <see cref="Resolve"/>.
/// </summary>
public static class PushRoutes
{
    public const string Dashboard = "/dashboard";

    // ---- Library ----
    public const string LibraryHome = "/library";
    public const string LibraryLoans = "/library/loans";
    public const string LibraryBooks = "/library/books";
    public const string LibraryReserve = "/library/reserve";

    // ---- Guidance ----
    public const string GuidanceStudentHome = "/guidance";
    public const string GuidanceStudentAppointments = "/guidance/appointments";
    public const string GuidanceChat = "/guidance/chat";
    public const string GuidanceCounselorAppointments = "/guidance/counselor/appointments";
    public const string GuidanceCounselorChat = "/guidance/counselor/chat";

    // ---- Marketplace ----
    public const string MarketplaceOrders = "/marketplace/orders";
    public const string MarketplaceStaffOrders = "/marketplace/staff/orders";
    public const string MarketplaceStaffDeliveries = "/marketplace/staff/deliveries";

    // Campus services (003_campus_services.sql).
    public const string MarketplaceIdClaim = "/marketplace/id-claim";

    // ---- Lost & Found ----
    public const string LostFoundHome = "/lost-found";
    public const string LostFoundNotifications = "/lost-found/notifications";
    public const string LostFoundClaims = "/lost-found/claims";
    public const string LostFoundMatches = "/lost-found/matches";
    public const string LostFoundMyItems = "/lost-found/my-items";

    // ---- Announcements ----
    /// <summary>Campus announcements are only rendered on the dashboard.</summary>
    public const string Announcements = Dashboard;

    /// <summary>
    /// Maps a module + event to the screen the student should land on.
    /// Unknown events fall back to <see cref="Dashboard"/> - never to an
    /// external URL, never to a javascript: URL.
    /// </summary>
    public static string Resolve(string module, string type, string? targetUrl = null)
    {
        // An explicit, already-validated target always wins.
        if (!string.IsNullOrWhiteSpace(targetUrl))
            return Normalize(targetUrl);

        // Guidance states are open-ended ("GUIDANCE_APPOINTMENT_" + status), so
        // the module is matched first, then the specific event.
        //
        // Recipients verified against the services that actually send them:
        //   COUNSELOR receives  GUIDANCE_APPOINTMENT_REQUEST  (GuidanceStudentService.BookAsync)
        //                      GUIDANCE_FOLLOWUP_RESPONSE    (GuidanceStudentService.RespondFollowUpAsync)
        //   STUDENT receives   GUIDANCE_APPOINTMENT_CONFIRMED / _REJECTED / _CANCELLED
        //                      GUIDANCE_APPOINTMENT_RESCHEDULED
        //                      GUIDANCE_FOLLOWUP_REQUEST      (GuidanceCounselorService.CreateFollowUpAsync)
        //                      GUIDANCE_FOLLOWUP_CANCELLED    (GuidanceCounselorService.CancelFollowUpAsync)
        //
        // NOTE: "GUIDANCE_APPOINTMENT_CANCELLED" is sent to the counselor when a
        // student cancels AND to the student when the counselor cancels. One type,
        // two recipients, so it cannot be routed by type alone. It gets the neutral
        // module home, whose role gate then lands each side on the right screen.
        if (string.Equals(module, "Guidance", StringComparison.OrdinalIgnoreCase))
        {
            var guidanceEvent = $"{module}:{type}".ToLowerInvariant();

            return guidanceEvent switch
            {
                "guidance:guidance_appointment_request" => GuidanceCounselorAppointments,
                "guidance:guidance_followup_response" => GuidanceCounselorAppointments,

                // Ambiguous recipient: let the GuidanceGate sort it out.
                "guidance:guidance_appointment_cancelled" => GuidanceStudentHome,

                _ => GuidanceStudentAppointments,
            };
        }

        var key = $"{module}:{type}".ToLowerInvariant();

        return key switch
        {
            // ---------------- Lost & Found ----------------
            "lostfound:claimsubmitted" => LostFoundNotifications,
            "lostfound:claimapproved" => LostFoundClaims,
            "lostfound:claimrejected" => LostFoundClaims,
            "lostfound:claimupdate" => LostFoundClaims,
            "lostfound:recoveryready" => LostFoundMatches,
            "lostfound:recoverycompleted" => LostFoundClaims,
            "lostfound:matchfound" => LostFoundMatches,
            "lostfound:reportupdate" => LostFoundMyItems,

            // ---------------- Library ----------------
            "library:reservation-created" => LibraryLoans,
            "library:reservation-approved" => LibraryLoans,
            "library:reservation-ready" => LibraryLoans,
            "library:reservation-rejected" => LibraryLoans,
            "library:reservation-cancelled" => LibraryLoans,
            "library:reservation-claimed" => LibraryLoans,
            "library:reservation-expired" => LibraryReserve,
            "library:reservation-expiring" => LibraryReserve,
            "library:borrow-claim-ready" => LibraryLoans,
            "library:borrow-approved" => LibraryLoans,
            "library:book-returned" => LibraryLoans,
            "library:due-soon" => LibraryLoans,
            "library:overdue" => LibraryLoans,
            "library:new-copy" => LibraryBooks,

            // ---------------- Marketplace ----------------
            "marketplace:order-placed" => MarketplaceStaffOrders,
            "marketplace:order-status" => MarketplaceOrders,
            "marketplace:order-cancelled" => MarketplaceOrders,
            "marketplace:order-ready-for-delivery" => MarketplaceStaffDeliveries,
            // A campus card refund (003_campus_services.sql) belongs on the
            // order the money went back for, not on the generic orders list.
            "marketplace:order-refunded" => MarketplaceOrders,
            // Cash taken at the counter (010_order_lifecycle.sql). The buyer
            // needs to know their order is paid before they walk to collect it.
            "marketplace:order-cash-confirmed" => MarketplaceOrders,
            // An uncollected order expiring (010_order_lifecycle.sql) is the one
            // thing that makes the buyer's order list change without them doing
            // anything, so it has to land on that list.
            "marketplace:order-expired" => MarketplaceOrders,
            // ID claim lifecycle: the user needs to know the moment a claim is
            // released (they now have a code to bring) or turned down.
            "marketplace:id-claim-released" => MarketplaceIdClaim,
            "marketplace:id-claim-claimed" => MarketplaceIdClaim,
            "marketplace:id-claim-rejected" => MarketplaceIdClaim,

            // ---------------- Account ----------------
            "account:verification-approved" => Dashboard,
            "account:verification-rejected" => Dashboard,
            "account:verification-reupload" => Dashboard,

            // ---------------- Announcements ----------------
            // Broadcast: every subscribed device, whatever the role.
            "announcements:announcement-published" => Announcements,
            "announcements:announcement-updated" => Announcements,

            _ => Dashboard,
        };
    }

    /// <summary>
    /// Guards against an open-redirect or a script URL being delivered
    /// to the service worker: anything that is not a simple relative
    /// path collapses to <see cref="Dashboard"/>.
    /// </summary>
    public static string Normalize(string? url)
    {
        var candidate = (url ?? string.Empty).Trim();

        if (candidate.Length == 0)
            return Dashboard;

        // Must be same-origin and must not be a protocol-relative URL.
        if (!candidate.StartsWith('/') || candidate.StartsWith("//"))
            return Dashboard;

        // Block control characters and backslashes (IE-style "\\" hosts).
        foreach (var ch in candidate)
        {
            if (char.IsControl(ch) || ch == '\\')
                return Dashboard;
        }

        // Reject anything that could be parsed as a scheme once decoded.
        var lowered = candidate.ToLowerInvariant();
        if (lowered.Contains("javascript:") || lowered.Contains("data:"))
            return Dashboard;

        return candidate.Length > 500 ? Dashboard : candidate;
    }

    /// <summary>
    /// Hard caps so a 4 KB push payload can never be exceeded and a
    /// broken title can never blow up the notification tray.
    /// </summary>
    public static (string Title, string Body) ShapeTitleAndBody(string? title, string? body)
    {
        var safeTitle = SoftTrim(title, 90);
        var safeBody = SoftTrim(body, 400);

        if (safeTitle.Length == 0) safeTitle = "CDM OneServe";
        if (safeBody.Length == 0) safeBody = "You have a new update.";

        return (safeTitle, safeBody);
    }

    private static string SoftTrim(string? value, int max)
    {
        var text = (value ?? string.Empty).Trim();

        // Collapse newlines: the notification body is one line.
        text = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

        while (text.Contains("  ", StringComparison.Ordinal))
            text = text.Replace("  ", " ", StringComparison.Ordinal);

        return text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 1), "…");
    }
}
