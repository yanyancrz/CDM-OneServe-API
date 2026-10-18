// ============================================================
// CAMPUSMARKET - storefront entities
//
// Created by Database/Marketplace/006_storefronts.sql (and repaired by 007).
//
// WHY A NEW LAYER
//   `marketplace_workspaces` is a FULFILMENT container: where stock lives and
//   which stall terminal is pinned to it. No owner, no team, no public page.
//   These tables are the SELLER'S SHOPFRONT on top of it.
//
//   THE BRIDGE is MarketplaceStore.WorkspaceId: a store fronts exactly one
//   workspace, so every existing workspace-scoped behaviour - reserved stock,
//   the order state machine, operator duty shifts, the staff order queue -
//   keeps working without modification.
//
// RELATIONSHIP TO THE EXISTING TEAM TABLE
//   `marketplace_stall_accounts` is deliberately untouched. That is the shared
//   TERMINAL login for a workspace. `marketplace_store_members` is a PERSON
//   with named permissions. Both can operate the same workspace; they answer
//   different questions and are not substitutes.
//
// NO ONLINE PAYMENTS in this phase. Cash and campus card only, so there is
// deliberately no payment_attempts / provider_event_inbox / webhook layer.
//
// Money is integer *_Centavos, matching marketplace_orders.
// ============================================================

namespace CDM_OneServe_API.Models.Marketplace;

/// <summary>What a storefront sells, used for the browse filters.</summary>
public static class MarketplaceStoreKind
{
    public const string Food = "food";
    public const string Supplies = "supplies";

    public static readonly string[] All = { Food, Supplies };

    public static bool IsValid(string? kind) =>
        kind is not null && All.Contains(kind, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? kind) =>
        string.Equals(kind, Supplies, StringComparison.OrdinalIgnoreCase) ? Supplies : Food;

    public static string Label(string? kind) =>
        Normalize(kind) == Supplies ? "Supplies" : "Food";
}

/// <summary>
/// A storefront's lifecycle.
///
///   active - visible in the browse list and accepts orders
///   paused - hidden from new orders; the OWNER reopens it
///   banned - hidden, set by an administrator
///   deleted - soft-deleted, media cleared, kept for order history
///
/// Unbanning lands on Paused, never Active: reopening is deliberately the
/// owner's decision, not the administrator's.
/// </summary>
public static class MarketplaceStoreStatus
{
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Banned = "banned";
    public const string Deleted = "deleted";

    public static readonly string[] All = { Active, Paused, Banned, Deleted };

    /// <summary>States a buyer may see and order from.</summary>
    public static readonly string[] Orderable = { Active };

    public static bool IsValid(string? status) =>
        status is not null && All.Contains(status, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? status) =>
        All.FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase))
        ?? Active;

    /// <summary>True when a buyer may browse this store and place orders with it.</summary>
    public static bool IsOrderable(string? status) =>
        string.Equals(Normalize(status), Active, StringComparison.Ordinal);

    /// <summary>True while the store still shows in public browsing.</summary>
    public static bool IsVisible(string? status, bool isActive)
    {
        if (!isActive) return false;
        return IsOrderable(status) || string.Equals(Normalize(status), Paused, StringComparison.Ordinal);
    }

    /// <summary>The owner may flip between these two; admin ban is separate.</summary>
    public static bool CanOwnerSet(string? status) =>
        Normalize(status) is Active or Paused;
}

/// <summary>
/// The five things a store member can be trusted to do.
///
/// These are the exact permissions the CampusMarket spec defines, kept as a
/// closed list in the database too (CHECK constraint on
/// marketplace_store_permissions.Permission), so a bad write cannot invent a
/// sixth one.
///
/// Plural, to avoid colliding with the <see cref="MarketplaceStorePermission"/>
/// entity - one row per granted bit.
/// </summary>
public static class MarketplaceStorePermissions
{
    /// <summary>Manage products and categories.</summary>
    public const string Catalog = "catalog";

    /// <summary>Prepare and advance orders.</summary>
    public const string Orders = "orders";

    /// <summary>Confirm a cash payment at the counter.</summary>
    public const string Cash = "cash";

    /// <summary>Read sales figures and the forecast.</summary>
    public const string Analytics = "analytics";

    /// <summary>Manage delivery and hand-over.</summary>
    public const string Delivery = "delivery";

    public static readonly string[] All = { Catalog, Orders, Cash, Analytics, Delivery };

    /// <summary>The permissions a new member gets when none are chosen.</summary>
    public static readonly string[] DefaultForNewMember = { Orders };

    public static bool IsValid(string? permission) =>
        permission is not null && All.Contains(permission, StringComparer.OrdinalIgnoreCase);

    /// <summary>Normalises and drops anything not on the allow-list.</summary>
    public static string[] Sanitize(IEnumerable<string>? requested)
    {
        var wanted = (requested ?? Array.Empty<string>())
            .Select(p => (p ?? "").Trim())
            .Where(IsValid)
            .Select(p => p.ToLowerInvariant())
            .Distinct()
            .ToList();

        return wanted.Count > 0 ? wanted.ToArray() : DefaultForNewMember;
    }

    public static string Label(string? permission) => permission switch
    {
        Catalog => "Manage products & categories",
        Orders => "Prepare & manage orders",
        Cash => "Confirm cash payments",
        Analytics => "View sales & forecast",
        Delivery => "Manage delivery & handover",
        _ => permission ?? ""
    };
}

/// <summary>Where a job application to join a store has got to.</summary>
public static class MarketplaceStoreApplicationStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    public static readonly string[] All = { Pending, Approved, Rejected };

    public static string Normalize(string? status) =>
        All.FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase))
        ?? Pending;
}

/// <summary>
/// A seller storefront.
///
/// A store is a public face; the workspace it points at is where the work
/// actually happens. `OwnerUserId` null means the Marketplace Head has not
/// assigned an owner yet - the storefront still browses, but no one can manage
/// it as a team.
/// </summary>
public class MarketplaceStore
{
    public int StoreId { get; set; }

    /// <summary>The owner. Null once the owning account is deleted - the store and its order history survive.</summary>
    public int? OwnerUserId { get; set; }

    public User? OwnerUser { get; set; }

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>food | supplies.</summary>
    public string Kind { get; set; } = MarketplaceStoreKind.Food;

    public string Location { get; set; } = "";

    public string? PhotoUrl { get; set; }

    public string? BannerUrl { get; set; }

    /// <summary>active | paused | banned | deleted.</summary>
    public string Status { get; set; } = MarketplaceStoreStatus.Active;

    /// <summary>Per-store delivery opt-in, on top of the system-wide setting.</summary>
    public bool DeliveryEnabled { get; set; }

    public int DeliveryFeeCentavos { get; set; }

    /// <summary>
    /// THE BRIDGE to the fulfilment model: the workspace holding this store's
    /// products, stock and orders. Null until wired up; a store without one
    /// cannot take orders.
    /// </summary>
    public int? WorkspaceId { get; set; }

    public MarketplaceWorkspace? Workspace { get; set; }

    /// <summary>Soft archive, independent of Status.</summary>
    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Someone who works at a store.
///
/// THE OWNER IS NOT A ROW HERE. Ownership lives on
/// <see cref="MarketplaceStore.OwnerUserId"/> and implies every permission, so
/// an owner can never be edited out of their own store or appear twice.
/// </summary>
public class MarketplaceStoreMember
{
    /// <summary>
    /// Surrogate key.
    ///
    /// The natural key is (StoreId, UserId), but it is NOT the primary key: this
    /// table's StoreId is the foreign key to marketplace_stores, and the
    /// permission rows need their own key to hang off. Keying both on StoreId
    /// would make one column serve as the FK for two principals at once, which
    /// EF resolves with a shadow property and a runtime
    /// "Unknown column MarketplaceStoreMemberStoreId" error. See 008.
    /// </summary>
    public int MemberId { get; set; }

    public int StoreId { get; set; }
    public MarketplaceStore? Store { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Free text chosen by the owner: "Cashier", "Kitchen", ...</summary>
    public string? Position { get; set; }

    public int? AssignedBy { get; set; }

    public User? AssignedByUser { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    public List<MarketplaceStorePermission> Permissions { get; set; } = new();
}

/// <summary>
/// One permission bit for one member.
///
/// A child table rather than a JSON array, so "who can manage the catalogue
/// across all stores" is a plain index seek. The owner is deliberately absent:
/// they have every permission without needing a row per bit.
/// </summary>
public class MarketplaceStorePermission
{
    /// <summary>The owning member. This is the foreign key - see 008.</summary>
    public int MemberId { get; set; }

    public MarketplaceStoreMember? Member { get; set; }

    /// <summary>
    /// Kept alongside MemberId for reporting and for the "who can do X in store
    /// Y" index seek. Never the foreign key - that is MemberId alone.
    /// </summary>
    public int StoreId { get; set; }

    public int UserId { get; set; }

    /// <summary>catalog | orders | cash | analytics | delivery.</summary>
    public string Permission { get; set; } = "";
}

/// <summary>
/// A request to join a store's team.
///
/// Approved by the STORE OWNER only - not an admin, not another member. Who
/// joins a team is the owner's call, unlike a seller application which an
/// administrator grants.
/// </summary>
public class MarketplaceStoreApplication
{
    public int ApplicationId { get; set; }

    public int StoreId { get; set; }
    public MarketplaceStore? Store { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>The position being asked for. The owner decides the real one.</summary>
    public string Position { get; set; } = "";

    public string Message { get; set; } = "";

    /// <summary>pending | approved | rejected.</summary>
    public string Status { get; set; } = MarketplaceStoreApplicationStatus.Pending;

    public int? ReviewedBy { get; set; }

    public User? ReviewedByUser { get; set; }

    public string? ReviewNote { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Database-computed marker for "this person has one pending application
    /// here". Read-only: it comes from the generated column in 006, and the
    /// UNIQUE key on it is what stops duplicate applications.
    /// </summary>
    public int? PendingUserId { get; set; }
}

/// <summary>
/// A product category, optionally scoped to one storefront.
///
/// <see cref="StoreId"/> null means a SYSTEM category, valid for any store.
/// That is what the legacy <c>SubCategory</c> strings backfilled into, so the
/// existing 16 products keep working untouched.
/// </summary>
public class MarketplaceCategory
{
    public int CategoryId { get; set; }

    /// <summary>Null = system-wide, usable by every store.</summary>
    public int? StoreId { get; set; }

    public MarketplaceStore? Store { get; set; }

    public string Name { get; set; } = "";

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Database-computed "which store does this belong to, with NULL folded to
    /// 0". UNIQUE on (StoreKey, Name) is what makes a system category actually
    /// unique - a plain UNIQUE(StoreId, Name) would accept unlimited NULLs.
    /// Read-only: it comes from the generated column in 006.
    /// </summary>
    public int? StoreKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// One gallery image for a product.
///
/// <c>products.ImageUrl</c> stays as the primary/thumbnail so everything that
/// already reads it keeps working. This holds the rest of the gallery, capped
/// at eight HTTPS URLs by the service - the same limit and URL rules the
/// CampusMarket spec uses.
/// </summary>
public class MarketplaceProductPhoto
{
    public int PhotoId { get; set; }

    public int ProductId { get; set; }
    public MarketplaceProduct? Product { get; set; }

    public string Url { get; set; } = "";

    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
