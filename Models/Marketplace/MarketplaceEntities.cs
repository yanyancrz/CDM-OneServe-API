namespace CDM_OneServe_API.Models.Marketplace;

// ============================================================
// CAMPUSMARKET - marketplace entities
//
// ARCHITECTURAL RULE
//   Marketplace is a MODULE INSIDE CDM OneServe. There is no marketplace
//   users table: every table that needs a person points at the EXISTING
//   `users`.`Id`. Student and Faculty accounts, and authentication, stay
//   owned by OneServe.
//
// Tables are created by Database/Marketplace/001_marketplace_schema.sql.
// The CHECK constraints there (stock never negative, quantity always > 0)
// are enforced by the database as well as by the service layer, so a bug
// cannot oversell.
// ============================================================

/// <summary>
/// One Marketplace Staff account.
///
/// The Marketplace Head (an `AdminModule = 'Marketplace'` user with Role
/// 'MarketplaceAdmin') manages the rows in this table - the same way the Library
/// Head and Guidance Head manage staff in their own modules. A Head is NOT a row
/// here: a Head is a SUPERSET of staff, and keeping the two apart is what lets an
/// operator be replaced without touching the Head.
/// </summary>
public class MarketplaceStaff
{
    public int UserId { get; set; }
    public User? User { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The Marketplace Head who assigned this account, so the Head can see who
    /// they put on the team. Null for rows created before this was tracked.
    /// </summary>
    public int? AssignedBy { get; set; }

    public User? Assigner { get; set; }

    /// <summary>
    /// Deactivating keeps the history of orders and messages that actor handled.
    /// It is the soft-delete that lets a Head "remove" a staff member safely.
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Marketplace-wide configuration, single row (Id = 1). Holds the pickup
/// location/instructions and the campus-delivery fee, so neither is ever
/// hardcoded in the API or the client.
/// </summary>
public class MarketplaceSettings
{
    public int Id { get; set; }
    public string PickupLocation { get; set; } = "CDM Marketplace - designated campus pickup area";
    public string PickupInstructions { get; set; } = "";
    public bool DeliveryEnabled { get; set; } = true;
    public int DeliveryFeeCentavos { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One row per calendar day, holding how many order references have been handed
/// out for that day. Backs the ORD-YYYYMMDD-NNNN format.
///
/// This exists so the sequence is allocated by an atomic
/// UPDATE ... ON DUPLICATE KEY UPDATE rather than by counting existing orders.
/// A count-then-insert races: two orders placed in the same second both read the
/// same count and collide on the UNIQUE OrderReference index.
public class MarketplaceOrderCounter
{
    /// <summary>The day the counter is for.</summary>
    public DateTime DayDate { get; set; }

    /// <summary>The highest number issued so far for that day.</summary>
    public int LastNumber { get; set; }
}

/// <summary>
/// An approved campus delivery destination.
///
/// Delivery is STRICTLY on campus, so the destination is chosen from this list
/// rather than typed as a free-form address - that is what makes it impossible
/// to enter a home address, barangay or city.
/// </summary>
public class MarketplaceCampusLocation
{
    public int LocationId { get; set; }
    public string LocationName { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>
    /// True when the user may add a specific room/office/area after choosing
    /// this, e.g. "Main Building" + "Room 204".
    /// </summary>
    public bool AllowsDetails { get; set; } = true;

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>A Food, Uniform or Merchandise product.</summary>
public class MarketplaceProduct
{
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    // ===== storefront categories (006_storefronts.sql) =====
    // Optional. The existing Category/SubCategory strings stay authoritative for
    // the 16 products that predate storefronts; CategoryId is for products a
    // store owner files under their own storefront category. Null until then.
    //
    // The navigation is StoreCategory, not Category: `Category` is already the
    // legacy string column (Food | Supplies | Uniforms | Merchandise).
    public int? CategoryId { get; set; }

    public MarketplaceCategory? StoreCategory { get; set; }

    /// <summary>Food | Uniforms | Merchandise.</summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// The workspace (stall or BusinessHub) that owns this product. Null only
    /// for rows predating workspaces; every new product is assigned one.
    /// </summary>
    public int? WorkspaceId { get; set; }
    public MarketplaceWorkspace? Workspace { get; set; }

    /// <summary>
    /// Food: Snacks | Drinks. Uniforms: the eight uniform types.
    /// Merchandise: CDM PIN | CDM Lace | CDM Jacket.
    /// </summary>
    public string SubCategory { get; set; } = "";

    public int PriceCentavos { get; set; }
    public string? ImageUrl { get; set; }

    /// <summary>True when the product has size variants (uniforms).</summary>
    public bool HasVariants { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<MarketplaceProductVariant> Variants { get; set; } = new();
}

/// <summary>
/// A size of a product, tracked with its own inventory.
///
/// Example: PE T-Shirt -> Small / Medium / Large / XL, each with a separate
/// stock quantity.
/// </summary>
public class MarketplaceProductVariant
{
    public int VariantId { get; set; }
    public int ProductId { get; set; }
    public MarketplaceProduct? Product { get; set; }

    /// <summary>"Size", "Color", ...</summary>
    public string VariantName { get; set; } = "";

    /// <summary>"Small", "2XL", ...</summary>
    public string VariantValue { get; set; } = "";

    /// <summary>Added to the product's base price (e.g. 2XL costs more).</summary>
    public int PriceAdjustmentCentavos { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Stock for one product+variant.
///
/// Available = StockQuantity - ReservedQuantity. Reserved is held by unpaid or
/// in-flight orders; it is released on cancel and consumed on complete.
/// </summary>
public class MarketplaceInventory
{
    public int InventoryId { get; set; }
    public int ProductId { get; set; }
    public MarketplaceProduct? Product { get; set; }

    /// <summary>Null for one-size products (no variants).</summary>
    public int? VariantId { get; set; }
    public MarketplaceProductVariant? Variant { get; set; }

    public int StockQuantity { get; set; }

    /// <summary>Held by unpaid / in-flight orders.</summary>
    public int ReservedQuantity { get; set; }

    /// <summary>At or below this, the product shows as "low stock" for staff.</summary>
    public int LowStockThreshold { get; set; } = 5;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>What a buyer can actually order right now.</summary>
    public int AvailableQuantity => StockQuantity - ReservedQuantity;
}

/// <summary>One cart line for the signed-in user.</summary>
public class MarketplaceCartItem
{
    public int CartItemId { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public int ProductId { get; set; }
    public MarketplaceProduct? Product { get; set; }
    public int? VariantId { get; set; }
    public MarketplaceProductVariant? Variant { get; set; }
    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A marketplace order. Pickup and CampusDelivery are two FULFILLMENT METHODS
/// of the same ordering system - same cart, same checkout, same inventory,
/// same staff dashboard.
/// </summary>
public class MarketplaceOrder
{
    public int OrderId { get; set; }

    /// <summary>
    /// The buyer. This is the existing OneServe user, never a duplicate.
    /// </summary>
    public int UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// The workspace that fulfils this order. One order per workspace -
    /// a checkout spanning stalls produces one order each.
    /// </summary>
    public int? WorkspaceId { get; set; }
    public MarketplaceWorkspace? Workspace { get; set; }

    /// <summary>Human readable, e.g. ORD-20261008-0001.</summary>
    public string OrderReference { get; set; } = "";

    /// <summary>
    /// Client idempotency key - one order per (UserId, key), so a double-tapped
    /// "Place order" returns the original instead of charging twice.
    /// </summary>
    public Guid IdempotencyKey { get; set; }

    /// <summary>
    /// The buyer's original checkout key, shared by every order one checkout
    /// produced. Lets a retried checkout (empty cart, response lost) return the
    /// original group instead of failing or duplicating.
    /// </summary>
    public Guid? RequestKey { get; set; }

    /// <summary>
    /// Pickup | CampusDelivery. Deliberately not a generic "Delivery" - campus
    /// delivery is not an external delivery service.
    /// </summary>
    public string FulfillmentMethod { get; set; } = "Pickup";

    // ===== CampusDelivery only. Null/empty for Pickup. =====

    public string? RecipientName { get; set; }
    public string? ContactNumber { get; set; }

    /// <summary>
    /// A row in marketplace_campus_locations. This is what enforces "delivery
    /// stays on campus" - the destination cannot be an arbitrary address.
    /// </summary>
    public int? CampusLocationId { get; set; }

    public MarketplaceCampusLocation? CampusLocation { get; set; }

    /// <summary>"Room 204", "Lab 3". Only when the location AllowsDetails.</summary>
    public string? SpecificLocation { get; set; }

    public string? DeliveryInstructions { get; set; }

    // ===== shared =====

    /// <summary>Buyer note on the order; applies to both methods.</summary>
    public string Notes { get; set; } = "";

    /// <summary>cash | campus_card. The column exists so online can be added later.</summary>
    public string PaymentMethod { get; set; } = "cash";

    // ===== pre-ordering (003_campus_services.sql) =====

    /// <summary>
    /// True when the buyer chose a pickup slot ahead of time (canteen
    /// pre-order). False for an ordinary "as soon as you can" order, which is
    /// every row that existed before pre-ordering shipped.
    /// </summary>
    public bool IsPreOrder { get; set; }

    /// <summary>
    /// The pickup slot the buyer asked for. Null on an ordinary order.
    /// Stored UTC, like every other timestamp in this module.
    /// </summary>
    public DateTime? ScheduledFor { get; set; }

    /// <summary>
    /// The short code the buyer shows at the counter to collect the order.
    /// Issued for pre-orders and for orders paid at the counter; null when
    /// the order is already settled. Unique - the UNIQUE index ignores the
    /// many nulls.
    /// </summary>
    public string? ClaimCode { get; set; }

    // ===== money, always integer centavos =====

    public int SubtotalCentavos { get; set; }

    /// <summary>Always 0 for Pickup. Read from marketplace_settings.</summary>
    public int DeliveryFeeCentavos { get; set; }

    public int TotalCentavos { get; set; }

    /// <summary>
    /// Pickup   : Pending | Confirmed | Preparing | ReadyForPickup | Completed | Cancelled
    /// Delivery : Pending | Confirmed | Preparing | OutForDelivery | Delivered | Completed | Cancelled
    /// </summary>
    public string Status { get; set; } = "Pending";

    /// <summary>unpaid | paid.</summary>
    public string PaymentStatus { get; set; } = "unpaid";

    // ===== order lifecycle (010_order_lifecycle.sql) =====

    /// <summary>
    /// True once this order no longer holds an outstanding reservation -
    /// whether it was RELEASED (cancelled, expired) or CONSUMED (completed).
    ///
    /// This is the IDEMPOTENCY LOCK for stock. Every settle path claims it with
    /// a conditional UPDATE
    /// (<c>WHERE OrderId = @id AND StockSettled = 0</c>) and only touches
    /// inventory when that update affects exactly one row, so two concurrent
    /// callers cannot both release the same hold.
    ///
    /// Without it, the only guard was "does this SKU have a hold of at least this
    /// much", which a second cancel satisfies using a DIFFERENT order's
    /// reservation - silently starving a live order.
    ///
    /// Named Settled rather than Released because a completed order consumes its
    /// hold rather than returning it.
    /// </summary>
    public bool StockSettled { get; set; }

    /// <summary>When cash was actually confirmed, if it was.</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>
    /// The collect-by deadline, or null when there is none yet.
    ///
    /// Deliberately NOT set at placement. In the CampusMarket original a 10-minute
    /// expiry works because <c>awaiting_payment</c> means an unfinished ONLINE
    /// payment. Here a cash order is unpaid from placement until it is collected
    /// at the counter, which is hours - so a short window would cancel almost
    /// every cash order before the kitchen started it.
    ///
    /// The deadline is set when the order becomes collectable (ReadyForPickup or
    /// Delivered), which is the moment it can actually be abandoned.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Why it expired, for the audit. e.g. "Not collected within 24 hours".</summary>
    public string? ExpiryReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    // ===== claim hand-over (003_campus_services.sql) =====

    /// <summary>When staff verified the claim code and released the order.</summary>
    public DateTime? ClaimedAt { get; set; }

    /// <summary>The staff member who released it. Null once the user is deleted.</summary>
    public int? ClaimedByUserId { get; set; }

    public User? ClaimedByUser { get; set; }

    public List<MarketplaceOrderItem> Items { get; set; } = new();
}

/// <summary>
/// One line on an order. Name, variant and price are SNAPSHOTS taken at purchase
/// time, so the history stays accurate after the product is renamed or repriced.
/// </summary>
public class MarketplaceOrderItem
{
    public int OrderItemId { get; set; }
    public int OrderId { get; set; }
    public MarketplaceOrder? Order { get; set; }

    /// <summary>Null once the product has been deleted.</summary>
    public int? ProductId { get; set; }

    public MarketplaceProduct? Product { get; set; }

    public int? VariantId { get; set; }
    public MarketplaceProductVariant? Variant { get; set; }

    public string ProductName { get; set; } = "";
    public string VariantName { get; set; } = "";
    public int Quantity { get; set; }
    public int UnitPriceCentavos { get; set; }
    public int SubtotalCentavos { get; set; }
}

/// <summary>
/// A Chat / Concerns thread. One per user; reopening a resolved thread reuses
/// the same row. OrderId links the thread to an order when relevant.
/// </summary>
public class MarketplaceConversation
{
    public int ConversationId { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Null when the question is not about a specific order.</summary>
    public int? OrderId { get; set; }

    public MarketplaceOrder? Order { get; set; }

    /// <summary>open | in_progress | resolved.</summary>
    public string Status { get; set; } = "open";

    public string Subject { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<MarketplaceMessage> Messages { get; set; } = new();
}

/// <summary>
/// One message in a thread.
///
/// SenderType is `user` or `staff`. The CDM OneServe Admin is NEVER `staff` -
/// they monitor only and cannot answer on behalf of Marketplace Staff.
/// </summary>
public class MarketplaceMessage
{
    public int MessageId { get; set; }
    public int ConversationId { get; set; }
    public MarketplaceConversation? Conversation { get; set; }

    /// <summary>user | staff.</summary>
    public string SenderType { get; set; } = "user";

    public int SenderId { get; set; }
    public string Message { get; set; } = "";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ============================================================
// PER-STALL ACCOUNTS + WORKSPACES (002_stall_workspaces.sql)
//
// Every stall has its OWN login account. After signing in with the
// stall account, the human on duty enters their OPERATOR NAME once;
// the server-validated operator session carries that name onto every
// inventory update. The account authenticates, the name attributes.
// ============================================================

/// <summary>
/// A FoodHub location (e.g. "Main FoodHub — Old Building Lobby") that
/// groups stalls. Managed by the Marketplace Head. Distinct from
/// <see cref="MarketplaceCampusLocation"/>, which are delivery
/// destinations, not stall groupings.
/// </summary>
public class MarketplaceStallLocation
{
    public int StallLocationId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One operable unit: a food stall, or the CDM BusinessHub workspace.
/// Products, inventory (via product) and orders each belong to exactly one.
/// </summary>
public class MarketplaceWorkspace
{
    public int WorkspaceId { get; set; }

    /// <summary>Stall | BusinessHub.</summary>
    public string WorkspaceType { get; set; } = "Stall";

    public string Name { get; set; } = "";

    /// <summary>Null for the BusinessHub workspace.</summary>
    public int? StallLocationId { get; set; }
    public MarketplaceStallLocation? StallLocation { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The login account of one workspace. One row per workspace, one user per
/// row: a stall account can never operate a different stall, because the
/// workspace is derived server-side from this link, never from the request.
/// </summary>
public class MarketplaceStallAccount
{
    public int WorkspaceId { get; set; }
    public MarketplaceWorkspace? Workspace { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public int? AssignedBy { get; set; }
    public User? Assigner { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The designated human operator named by the Head when the login was
    /// created, with the last 3 digits of their ID number. Shown on the
    /// setup screen with a reminder; matched (not trusted blindly) when a
    /// shift starts.
    /// </summary>
    public string? OperatorName { get; set; }
    public string? IdLast3 { get; set; }
}

/// <summary>
/// The validated context of one duty shift: who signed in (UserId), which
/// workspace is operated, and the HUMAN name entered after login. Mutations
/// require an active session; the operator name on audit rows always comes
/// from here, never from client input.
/// </summary>
public class MarketplaceOperatorSession
{
    public Guid SessionId { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public int WorkspaceId { get; set; }
    public MarketplaceWorkspace? Workspace { get; set; }
    public string OperatorName { get; set; } = "";

    /// <summary>Last 3 ID digits, attached when the name matches the designated operator.</summary>
    public string? IdLast3 { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? EndedAt { get; set; }
}

/// <summary>
/// One stock movement, append-only. Written by staff updates (with the
/// session's operator name), by checkout (reservation), by completion
/// (sale) and by cancellation (release).
/// </summary>
public class MarketplaceInventoryTransaction
{
    public int TransactionId { get; set; }
    public int? ProductId { get; set; }
    public MarketplaceProduct? Product { get; set; }
    public int? VariantId { get; set; }
    public MarketplaceProductVariant? Variant { get; set; }
    public int WorkspaceId { get; set; }
    public MarketplaceWorkspace? Workspace { get; set; }

    /// <summary>
    /// The signed-in account that caused this (stall account, head, buyer...).
    ///
    /// Null when that user is deleted, and null for movements a background
    /// schedule caused - a system-initiated stock release has no human to
    /// attribute it to, and the audit should record that rather than inventing a
    /// user id. See 011_system_audit_actor.sql.
    /// </summary>
    public int? ActorUserId { get; set; }

    /// <summary>
    /// The human on duty, from the validated operator session. Null for
    /// buyer-caused movements (reservations), which have no operator.
    /// </summary>
    public string? OperatorName { get; set; }

    /// <summary>Last 3 ID digits, when the session name matched the designated operator.</summary>
    public string? IdLast3 { get; set; }

    public int PreviousQuantity { get; set; }
    public int QuantityChanged { get; set; }
    public int NewQuantity { get; set; }

    /// <summary>
    /// InitialStock | Restock | Adjustment | Reservation | Sale |
    /// Cancellation | Damage | Correction.
    /// </summary>
    public string ActionType { get; set; } = "";

    public string? Reason { get; set; }
    public int? OrderId { get; set; }
    public MarketplaceOrder? Order { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}