namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Vocabulary for the marketplace. One place, so the API, the database seed and
/// the frontend cannot drift apart.
/// </summary>
public static class MarketplaceCategories
{
    public const string Food = "Food";
    public const string Uniforms = "Uniforms";
    public const string Merchandise = "Merchandise";

    /// <summary>
    /// The Paper Shop. Stationery, pens, notebooks and anything else the
    /// registrar's office sells. Added by 003_campus_services.sql.
    /// </summary>
    public const string Supplies = "Supplies";

    /// <summary>The top-level categories, in the order the UI shows them.</summary>
    public static readonly string[] All = { Food, Supplies, Uniforms, Merchandise };

    /// <summary>Food splits into Snacks and Drinks.</summary>
    public static readonly string[] FoodSubCategories = { "Snacks", "Drinks", "Meals", "Beverages" };

    /// <summary>The uniform types from the spec.</summary>
    public static readonly string[] UniformSubCategories =
    {
        "Wash Day Uniform",
        "Institute Shirt",
        "PE Uniform Set",
        "PE Pants",
        "PE T-Shirt",
        "School Uniform Set",
        "School Uniform Blouse",
        "School Uniform Pants"
    };

    /// <summary>The merchandise from the spec.</summary>
    public static readonly string[] MerchandiseSubCategories = { "CDM PIN", "CDM Lace", "CDM Jacket" };

    /// <summary>The Paper Shop sections, added by 003_campus_services.sql.</summary>
    public static readonly string[] SuppliesSubCategories =
    {
        "Writing",        // pens, pencils, markers
        "Notebooks",      // pads, paper, notebooks
        "Paper Products", // bond paper, art paper, envelopes
        "School Tools",   // scissors, glue, rulers, calculators
        "Exam Supplies"   // answer sheets, dry seal, pens for exams
    };

    public static bool IsValidCategory(string? category) =>
        category is not null && All.Contains(category, StringComparer.OrdinalIgnoreCase);

    public static bool IsValidSubCategory(string category, string? sub) =>
        (sub ?? "").Trim() is { Length: > 0 } value && SubCategoriesFor(category).Contains(value, StringComparer.OrdinalIgnoreCase);

    public static string[] SubCategoriesFor(string category) => category switch
    {
        Food => FoodSubCategories,
        Supplies => SuppliesSubCategories,
        Uniforms => UniformSubCategories,
        Merchandise => MerchandiseSubCategories,
        _ => Array.Empty<string>()
    };

    /// <summary>
    /// Uniforms are sized, so they need size variants. Food, supplies and
    /// merchandise are one-size, which is why only uniforms get a variant
    /// dropdown by default - but the field stays open so staff can add
    /// variants to anything later.
    /// </summary>
    public static bool DefaultHasVariants(string category) =>
        category == Uniforms;
}

/// <summary>
/// Fulfillment methods.
///
/// Deliberately NOT a generic "Delivery": campus delivery is not an external
/// delivery service, so the value says CampusDelivery and every caller is
/// forced to supply an approved campus location.
/// </summary>
public static class MarketplaceFulfillment
{
    public const string Pickup = "Pickup";
    public const string CampusDelivery = "CampusDelivery";

    public static readonly string[] All = { Pickup, CampusDelivery };

    public static bool IsValid(string? method) =>
        method is not null && All.Contains(method, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? method) =>
        method == CampusDelivery ? CampusDelivery : Pickup;

    public static string Label(string? method) =>
        method == CampusDelivery ? "Campus Delivery" : "Pick Up";
}

/// <summary>
/// Order status, and the only legal paths through it.
///
/// Pick Up   : Pending -&gt; Confirmed -&gt; Preparing -&gt; ReadyForPickup -&gt; Completed
/// Delivery : Pending -&gt; Confirmed -&gt; Preparing -&gt; OutForDelivery -&gt; Delivered -&gt; Completed
/// Either way, Cancelled is reachable from any step before Completed.
///
/// A Pick Up order can never be pushed down the delivery path, and vice versa -
/// the next step always comes from this table, never from the client.
/// </summary>
public static class MarketplaceOrderStatus
{
    public const string Pending = "Pending";
    public const string Confirmed = "Confirmed";
    public const string Preparing = "Preparing";
    public const string ReadyForPickup = "ReadyForPickup";
    public const string OutForDelivery = "OutForDelivery";
    public const string Delivered = "Delivered";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    /// <summary>
    /// A buyer asked to return a paid order. Added by 014_refunds.sql.
    ///
    /// It is deliberately a status of its own rather than "Cancelled", because
    /// the money has already changed hands - the buyer is asking for it back, not
    /// calling the order off. That distinction is what lets the audit trail answer
    /// "how many orders were returned" without counting cancellations.
    /// </summary>
    public const string ReturnRequested = "ReturnRequested";

    /// <summary>
    /// The refund was confirmed and the money went back. Added by
    /// 014_refunds.sql.
    /// </summary>
    public const string Refunded = "Refunded";

    /// <summary>
    /// Ready to be collected, and nobody came for it.
    ///
    /// Added by 010_order_lifecycle.sql, and deliberately SEPARATE from
    /// Cancelled. "You did not turn up" and "somebody called it off" are
    /// different facts: the first is the buyer's, the second usually is not, and
    /// conflating them makes the history lie to whoever reads it later.
    ///
    /// It is also the ONLY way a collectable order's hold is released.
    /// <see cref="CanCancel"/> forbids cancelling once an order is
    /// ReadyForPickup, so without a distinct terminal status an uncollected
    /// order would hold its stock forever with no path out.
    /// </summary>
    public const string Expired = "Expired";

    private static readonly string[] PickupFlow =
        { Pending, Confirmed, Preparing, ReadyForPickup, Completed };

    private static readonly string[] DeliveryFlow =
        { Pending, Confirmed, Preparing, OutForDelivery, Delivered, Completed };

    /// <summary>The ordered steps for a fulfillment method, excluding Cancelled.</summary>
    public static IReadOnlyList<string> Flow(string? method) =>
        MarketplaceFulfillment.Normalize(method) == MarketplaceFulfillment.CampusDelivery
            ? DeliveryFlow
            : PickupFlow;

    /// <summary>The next status staff may set, or null when the order is done.</summary>
    public static string? Next(string? method, string? current)
    {
        var flow = Flow(method).ToList();
        var index = flow.IndexOf(current ?? "");

        return index < 0 || index >= flow.Count - 1 ? null : flow[index + 1];
    }

    /// <summary>True when <paramref name="to"/> is exactly one step after <paramref name="from"/>.</summary>
    public static bool IsAllowedNext(string? method, string? from, string? to) =>
        Next(method, from) is { } next && string.Equals(next, to, StringComparison.Ordinal);

    /// <summary>
    /// Cancelling is allowed while the order is still moving and has not been
    /// handed over. Delivered and Completed are final.
    /// </summary>
    public static bool CanCancel(string? status) =>
        status is not (Completed or Cancelled or Delivered or ReadyForPickup or OutForDelivery);

    /// <summary>True once stock should be permanently consumed.</summary>
    public static bool ConsumesInventory(string? status) =>
        status == Completed;
}

/// <summary>
/// When an order that is ready for collection stops being waited for.
///
/// WHY THIS EXISTS, and why it is not the original's ten minutes
///   CampusMarket expires an <c>awaiting_payment</c> order after ten minutes,
///   which is right there because that status means "you have not finished an
///   ONLINE payment". The window is the payment session.
///
///   Here a cash order is <c>unpaid</c> from the moment it is placed until the
///   buyer collects it at the counter. That is hours. A ten-minute rule would
///   cancel nearly every cash order before the kitchen had started it, so
///   porting it would break the core workflow rather than fix it.
///
///   The deadline therefore starts where abandonment actually begins: when the
///   order is sitting at the counter waiting. Before that, staff are actively
///   preparing it and it is not neglected.
///
/// THE WINDOW
///   Long enough to cover "I ordered this before closing and will collect it
///   tomorrow", because that is a real and legitimate campus habit. Short enough
///   that the hold does not sit on the shelf for a week.
/// </summary>
public static class MarketplaceOrderExpiry
{
    /// <summary>
    /// How long a collectable order waits before it expires.
    ///
    /// Applied when the order REACHES ReadyForPickup or Delivered, never at
    /// placement. See the type summary for why.
    /// </summary>
    public static readonly TimeSpan CollectableWindow = TimeSpan.FromHours(24);

    /// <summary>Shown to the buyer when their order expires.</summary>
    public const string UncollectedReason = "Not collected within 24 hours";

    /// <summary>
    /// The statuses a buyer could still walk in and collect. An order only starts
    /// ageing once it is one of these.
    /// </summary>
    public static readonly string[] CollectableStatuses =
    {
        MarketplaceOrderStatus.ReadyForPickup,
        MarketplaceOrderStatus.Delivered
    };

    /// <summary>True once an order is waiting at the counter to be collected.</summary>
    public static bool IsCollectable(string? status) =>
        CollectableStatuses.Contains(status ?? "", StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The deadline for an order that has just become collectable, or null when
    /// it has not (or never will).
    /// </summary>
    public static DateTime? DeadlineFor(string? status, DateTime nowUtc) =>
        IsCollectable(status) ? nowUtc.Add(CollectableWindow) : null;

    /// <summary>
    /// True when an order may be expired right now: still collectable, past its
    /// deadline, and never claimed.
    ///
    /// <paramref name="expiresAt"/> being null means no deadline was ever set -
    /// an order created before 010_order_lifecycle.sql, or one that has not
    /// become collectable yet. Those are left alone deliberately; see the note in
    /// that migration about not backdating deadlines onto live orders.
    /// </summary>
    public static bool ShouldExpire(string? status, DateTime? expiresAt, DateTime nowUtc) =>
        IsCollectable(status) && expiresAt.HasValue && expiresAt.Value < nowUtc;
}

/// <summary>
/// How an order is paid.
///
/// cash          - pay at the counter when you collect (the original way)
/// campus_card   - spend the balance on the campus card, debited immediately
///
/// A campus_card order is paid the moment it is placed, which is what lets a
/// pre-order be committed to a slot without the buyer queueing with cash.
/// The card's balance is the only balance; there is no online gateway here.
public static class MarketplacePaymentMethod
{
    public const string Cash = "cash";

    /// <summary>Spend the marketplace_campus_cards balance.</summary>
    public const string CampusCard = "campus_card";

    public static readonly string[] All = { Cash, CampusCard };

    public static bool IsValid(string? method) =>
        method is not null && All.Contains(method, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? method) =>
        string.Equals(method, CampusCard, StringComparison.OrdinalIgnoreCase) ? CampusCard : Cash;

    public static string Label(string? method) =>
        Normalize(method) == CampusCard ? "Campus Card" : "Cash on Pickup";

    /// <summary>
    /// True when this method is settled without a human. Only the campus card
    /// is, and it is debited inside the same server-side transaction as the
    /// order - never by the client.
    /// </summary>
    public static bool IsSettledImmediately(string? method) => Normalize(method) == CampusCard;
}

/// <summary>
/// Pre-ordering, added by 003_campus_services.sql.
///
/// An ordinary order is fulfilled as soon as the stall can get to it. A
/// pre-order names a pickup slot the buyer chose, so the canteen can batch
/// cook and the buyer knows exactly when to come back.
/// </summary>
public static class MarketplacePreOrder
{
    /// <summary>
    /// How far ahead a slot may be booked. Canteen food is not worth eating
    /// next week, so the window is deliberately short.
    /// </summary>
    public static readonly TimeSpan MaxLeadTime = TimeSpan.FromDays(7);

    /// <summary>
    /// How late a slot may be relative to now. Keeps "reserve tomorrow 2am"
    /// out, which staff would have to work for anyway.
    /// </summary>
    public static readonly TimeSpan MinLeadTime = TimeSpan.FromMinutes(15);

    /// <summary>Pre-orders are always collected, never delivered to a room.</summary>
    public const string FulfillmentMethod = MarketplaceFulfillment.Pickup;

    /// <summary>
    /// Returns null when the slot is acceptable, or the reason it is not.
    /// A null <paramref name="scheduledFor"/> means "not a pre-order", which is
    /// always allowed.
    /// </summary>
    public static string? Validate(DateTime? scheduledFor, DateTime utcNow)
    {
        if (scheduledFor is not { } slot) return null;

        if (slot < utcNow.Add(MinLeadTime))
            return $"Choose a pickup slot at least {MinLeadTime.TotalMinutes:0} minutes from now.";

        if (slot > utcNow.Add(MaxLeadTime))
            return $"Pickup slots can only be booked up to {MaxLeadTime.TotalDays} days ahead.";

        return null;
    }

    public static bool IsValid(DateTime? scheduledFor, DateTime utcNow) =>
        Validate(scheduledFor, utcNow) is null;
}

/// <summary>Chat / Concerns thread states.</summary>
public static class MarketplaceChatStatus
{
    public const string Open = "open";
    public const string InProgress = "in_progress";
    public const string Resolved = "resolved";

    public static readonly string[] All = { Open, InProgress, Resolved };
}

/// <summary>Who sent a chat message. The OneServe Admin is never `staff`.</summary>
public static class MarketplaceSender
{
    public const string User = "user";
    public const string Staff = "staff";
}