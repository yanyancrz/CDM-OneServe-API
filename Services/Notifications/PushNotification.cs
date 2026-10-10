namespace CDM_OneServe_API.Services.Notifications;

/// <summary>
/// One browser notification to deliver. Everything is already final:
/// the caller resolved the text and the deep link, the dispatcher only
/// sanitises size, adds the dedupe key and writes the outbox row.
/// </summary>
/// <param name="UserId">Recipient. Always the authenticated owner.</param>
/// <param name="Module">LostFound | Library | Guidance | Marketplace | Account</param>
/// <param name="Type">Event tag, e.g. "ClaimApproved".</param>
/// <param name="Title">Short line, max ~90 characters.</param>
/// <param name="Body">Sentence, max ~400 characters.</param>
/// <param name="TargetUrl">Relative app path opened on click.</param>
/// <param name="DedupeKey">
/// Stable identity of the business event (e.g. "claimapproved:821").
/// The dispatcher appends the UTC date, so the same event fires at most
/// one push per day and a retry/double-tap cannot spam the student.
/// </param>
/// <param name="Tag">OS notification tray grouping key (optional).</param>
/// <param name="Priority">1 = act now, 9 = reminder.</param>
/// <param name="PushSubscriptionId">
/// Set only for a single-device send (test / "notify this browser").
/// NULL = every active subscription of the user.
/// </param>
/// <param name="OnceOnly">
/// True for "remind once" events (library due-soon, overdue): the
/// dedupe key has no date suffix, so the notification can only ever be
/// delivered one time. False (the default) buckets the key by UTC date,
/// so a state change can repeat on a later day.
/// </param>
public sealed record PushNotification(
    int UserId,
    string Module,
    string Type,
    string Title,
    string Body,
    string TargetUrl,
    string DedupeKey,
    string? Tag = null,
    byte Priority = 5,
    int? PushSubscriptionId = null,
    bool OnceOnly = false);
