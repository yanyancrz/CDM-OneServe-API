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

    /// <summary>The three top-level categories, in the order the UI shows them.</summary>
    public static readonly string[] All = { Food, Uniforms, Merchandise };

    /// <summary>Food splits into Snacks and Drinks.</summary>
    public static readonly string[] FoodSubCategories = { "Snacks", "Drinks" };

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

    public static bool IsValidCategory(string? category) =>
        category is not null && All.Contains(category, StringComparer.OrdinalIgnoreCase);

    public static bool IsValidSubCategory(string category, string? sub) =>
        (sub ?? "").Trim() is { Length: > 0 } value && SubCategoriesFor(category).Contains(value, StringComparer.OrdinalIgnoreCase);

    public static string[] SubCategoriesFor(string category) => category switch
    {
        Food => FoodSubCategories,
        Uniforms => UniformSubCategories,
        Merchandise => MerchandiseSubCategories,
        _ => Array.Empty<string>()
    };

    /// <summary>
    /// Uniforms are sized, so they need size variants. Food and merchandise are
    /// one-size, which is why only uniforms get a variant dropdown by default -
    /// but the field stays open so staff can add variants to anything later.
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