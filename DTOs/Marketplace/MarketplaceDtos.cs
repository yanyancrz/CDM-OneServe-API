using System.ComponentModel.DataAnnotations;
using CDM_OneServe_API.Models.Marketplace;

namespace CDM_OneServe_API.DTOs.Marketplace;

// ============================================================
// CAMPUSMARKET DTOs
//
// Names follow the module convention: *Dto for reads, *Request for commands.
// Money is always integer *_Centavos - never a float.
// No DTO here carries a password or a OneServe mutation: the marketplace only
// READS the shared user, it never writes to it.
// ============================================================

// ------------------------------------------------------------
// Identity (read from the EXISTING OneServe user)
// ------------------------------------------------------------

/// <summary>
/// The buyer, as the marketplace sees them. Every field is read from the
/// shared OneServe account - nothing is stored twice.
/// </summary>
public class MarketplaceBuyerDto
{
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string IdNumber { get; set; } = "";
    public string Email { get; set; } = "";
    /// <summary>Student | Faculty, from the OneServe role.</summary>
    public string AccountType { get; set; } = "";
    public string Course { get; set; } = "";
    public string YearLevel { get; set; } = "";
    public string Institute { get; set; } = "";
    public string ContactNumber { get; set; } = "";
}

// ------------------------------------------------------------
// Campus locations
// ------------------------------------------------------------

public class MarketplaceCampusLocationDto
{
    public int LocationId { get; set; }
    public string LocationName { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>True when the user may add a room/office/area after choosing this.</summary>
    public bool AllowsDetails { get; set; }

    public int SortOrder { get; set; }
}

public class SaveCampusLocationRequest
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string LocationName { get; set; } = "";

    [StringLength(300)]
    public string Description { get; set; } = "";

    public bool AllowsDetails { get; set; } = true;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

// ------------------------------------------------------------
// Products
// ------------------------------------------------------------

/// <summary>
/// A product as a buyer sees it, including live stock so an out-of-stock item
/// can never be added to the cart.
/// </summary>
public class MarketplaceProductDto
{
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string SubCategory { get; set; } = "";
    public int PriceCentavos { get; set; }
    public string ImageUrl { get; set; } = "";
    public bool HasVariants { get; set; }
    public bool IsActive { get; set; }

    /// <summary>The workspace (stall / BusinessHub) that owns this product.</summary>
    public int? WorkspaceId { get; set; }
    public string WorkspaceName { get; set; } = "";
    public string WorkspaceType { get; set; } = "";
    public string StallLocationName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Total stock across every variant (or the single row for one-size).</summary>
    public int StockQuantity { get; set; }
    public int ReservedQuantity { get; set; }

    /// <summary>Stock - reserved. What the buyer can actually order.</summary>
    public int AvailableQuantity { get; set; }

    public bool IsOutOfStock => AvailableQuantity <= 0;

    /// <summary>True when at least one variant is still orderable.</summary>
    public bool IsAvailable => AvailableQuantity > 0;

    public List<MarketplaceVariantDto> Variants { get; set; } = new();
}

public class MarketplaceVariantDto
{
    public int VariantId { get; set; }
    public int ProductId { get; set; }
    public string VariantName { get; set; } = "";
    public string VariantValue { get; set; } = "";
    public int PriceAdjustmentCentavos { get; set; }

    /// <summary>Base price plus this variant's adjustment.</summary>
    public int EffectivePriceCentavos { get; set; }

    public int StockQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int AvailableQuantity { get; set; }
    public bool IsOutOfStock => AvailableQuantity <= 0;
    public bool IsActive { get; set; }
}

public class SaveProductRequest
{
    public int? ProductId { get; set; }

    [Required, StringLength(150, MinimumLength = 2)]
    public string Name { get; set; } = "";

    [StringLength(2000)]
    public string Description { get; set; } = "";

    [Required]
    public string Category { get; set; } = "";

    [StringLength(48)]
    public string SubCategory { get; set; } = "";

    [Range(0, 100000000)]
    public int PriceCentavos { get; set; }

    // No ImageUrl here ON PURPOSE. A product photo is an uploaded file, set
    // only through POST /api/marketplace/staff/products/{id}/image. Accepting a
    // path (or a URL) in this payload would leave a second way in: any caller
    // could point ImageUrl at an arbitrary string, including one outside the
    // uploads folder.
    public bool HasVariants { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Required when creating a product as an operator WITHOUT a fixed stall
    /// account (Head, legacy personal staff): which workspace owns it. Stall
    /// accounts always use their own workspace and must not send this.
    /// Ignored on update (products never move between workspaces).
    /// </summary>
    public int? WorkspaceId { get; set; }

    /// <summary>
    /// When supplied, the variant set is replaced wholesale: variants missing
    /// from this list are deactivated. Keeps sizes and stock in one payload.
    /// </summary>
    public List<SaveVariantRequest>? Variants { get; set; }
}

public class SaveVariantRequest
{
    public int? VariantId { get; set; }

    [StringLength(48)]
    public string VariantName { get; set; } = "Size";

    [Required, StringLength(48, MinimumLength = 1)]
    public string VariantValue { get; set; } = "";

    public int PriceAdjustmentCentavos { get; set; }

    public int StockQuantity { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

// ------------------------------------------------------------
// Inventory
// ------------------------------------------------------------

/// <summary>
/// One inventory row for the staff Inventory screen. Reserved and available are
/// both shown so staff can see why something is unavailable.
/// </summary>
public class MarketplaceInventoryDto
{
    public int InventoryId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string Category { get; set; } = "";
    public string SubCategory { get; set; } = "";
    public int? VariantId { get; set; }
    public string VariantName { get; set; } = "";
    public int PriceCentavos { get; set; }
    public string ImageUrl { get; set; } = "";

    public int StockQuantity { get; set; }
    public int ReservedQuantity { get; set; }

    /// <summary>Available = Current - Reserved.</summary>
    public int AvailableQuantity => StockQuantity - ReservedQuantity;

    public int LowStockThreshold { get; set; }

    /// <summary>out_of_stock | low | healthy.</summary>
    public string StockState =>
        AvailableQuantity <= 0 ? "out_of_stock" : AvailableQuantity <= LowStockThreshold ? "low" : "healthy";

    public DateTime UpdatedAt { get; set; }
}

public class SaveInventoryRequest
{
    /// <summary>Sets the on-hand quantity. Reserved is never changed by staff.</summary>
    [Range(0, int.MaxValue)]
    public int StockQuantity { get; set; }

    [Range(0, int.MaxValue)]
    public int LowStockThreshold { get; set; } = 5;

    /// <summary>Why the count changed (damage, recount...). Shown in the audit trail.</summary>
    [StringLength(500)]
    public string Reason { get; set; } = "";
}

/// <summary>Adding stock. Additive, so a miscount tops up rather than overwrites.</summary>
public class RestockInventoryRequest
{
    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    /// <summary>Why stock was added. Shown in the audit trail.</summary>
    [StringLength(500)]
    public string Reason { get; set; } = "";
}

// ------------------------------------------------------------
// Cart
// ------------------------------------------------------------

public class MarketplaceCartItemDto
{
    public int CartItemId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string Category { get; set; } = "";
    public string SubCategory { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public int? VariantId { get; set; }
    public string VariantName { get; set; } = "";
    public int UnitPriceCentavos { get; set; }
    public int Quantity { get; set; }
    public int SubtotalCentavos { get; set; }

    /// <summary>The stall / BusinessHub selling this line.</summary>
    public int? WorkspaceId { get; set; }
    public string WorkspaceName { get; set; } = "";

    /// <summary>Stock still available for this exact line.</summary>
    public int AvailableQuantity { get; set; }

    /// <summary>False when the line cannot be ordered any more.</summary>
    public bool IsAvailable => AvailableQuantity >= Quantity;
}

/// <summary>
/// Adding a line to the cart. VariantId is required for a sized product
/// (uniforms) and ignored for a one-size product.
/// </summary>
public class AddCartItemRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    /// <summary>The chosen size. Must belong to this product when one is given.</summary>
    public int? VariantId { get; set; }

    [Range(1, 100)]
    public int Quantity { get; set; } = 1;
}

public class SetCartQuantityRequest
{
    /// <summary>0 or less removes the line.</summary>
    [Range(0, 100)]
    public int Quantity { get; set; }
}

// ------------------------------------------------------------
// Orders
// ------------------------------------------------------------

public class MarketplaceOrderDto
{
    public int OrderId { get; set; }

    /// <summary>ORD-20261008-0001 - what the buyer quotes when asking about it.</summary>
    public string OrderReference { get; set; } = "";

    public int UserId { get; set; }
    public string BuyerName { get; set; } = "";
    /// <summary>Student | Faculty, read from OneServe.</summary>
    public string BuyerAccountType { get; set; } = "";

    /// <summary>Pickup | CampusDelivery.</summary>
    public string FulfillmentMethod { get; set; } = "";
    public string FulfillmentLabel { get; set; } = "";

    // ===== CampusDelivery only =====

    public string? RecipientName { get; set; }
    public string? ContactNumber { get; set; }
    public int? CampusLocationId { get; set; }
    public string? CampusLocationName { get; set; }
    public string? SpecificLocation { get; set; }
    public string? DeliveryInstructions { get; set; }

    // ===== shared =====

    public string Notes { get; set; } = "";
    public string PaymentMethod { get; set; } = "cash";
    public string PaymentMethodLabel { get; set; } = "";

    // ===== pre-ordering + claim (003_campus_services.sql) =====

    public bool IsPreOrder { get; set; }

    /// <summary>The pickup slot the buyer booked. Null on an ordinary order.</summary>
    public DateTime? ScheduledFor { get; set; }

    /// <summary>What the buyer shows at the counter to collect this order.</summary>
    public string? ClaimCode { get; set; }

    /// <summary>True once staff have verified the code and released the order.</summary>
    public bool IsClaimed { get; set; }

    public DateTime? ClaimedAt { get; set; }
    public string ClaimedByName { get; set; } = "";

    /// <summary>The stall / BusinessHub fulfilling this order.</summary>
    public int? WorkspaceId { get; set; }
    public string WorkspaceName { get; set; } = "";

    public int SubtotalCentavos { get; set; }
    public int DeliveryFeeCentavos { get; set; }
    public int TotalCentavos { get; set; }

    public string Status { get; set; } = "";
    public string PaymentStatus { get; set; } = "";

    /// <summary>The ordered steps for this fulfillment method, for the timeline.</summary>
    public List<string> Timeline { get; set; } = new();

    /// <summary>Every timeline step up to and including the current one.</summary>
    public List<string> TimelineDone { get; set; } = new();

    /// <summary>How far along the timeline is, 0..Timeline.Count.</summary>
    public int TimelineStep { get; set; }

    /// <summary>What staff should press next, or null when the order is finished.</summary>
    public string? NextStatus { get; set; }

    public string NextStatusLabel { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public List<MarketplaceOrderItemDto> Items { get; set; } = new();
}

public class MarketplaceOrderItemDto
{
    public int OrderItemId { get; set; }
    public int? ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int? VariantId { get; set; }
    public string VariantName { get; set; } = "";
    public int Quantity { get; set; }
    public int UnitPriceCentavos { get; set; }
    public int SubtotalCentavos { get; set; }
}

/// <summary>
/// The answer to one checkout: one order PER workspace, because stalls
/// fulfil independently. A single-stall cart returns exactly one order.
/// </summary>
public class MarketplacePlacedOrdersDto
{
    public List<MarketplaceOrderDto> Orders { get; set; } = new();
}

public class PlaceMarketplaceOrderRequest
{
    /// <summary>
    /// Client idempotency key - reusing it returns the original order instead of
    /// creating a second one when a tap is double-registered.
    /// </summary>
    public Guid RequestKey { get; set; }

    [Required]
    public string FulfillmentMethod { get; set; } = "Pickup";

    [StringLength(16)]
    public string PaymentMethod { get; set; } = "cash";

    // ===== pre-ordering (003_campus_services.sql) =====

    /// <summary>
    /// The pickup slot the buyer wants, in UTC. Non-null makes this a
    /// pre-order; the canteen batches it instead of cooking on arrival.
    ///
    /// Ignored - and the order forced to Pickup - when FulfillmentMethod is
    /// CampusDelivery, because a meal taken to a room is not a timed pickup.
    /// The window (15 minutes to 7 days ahead) is validated by
    /// MarketplacePreOrder.Validate, never trusted from here.
    /// </summary>
    public DateTime? ScheduledFor { get; set; }

    // ===== CampusDelivery only. Required for delivery, ignored for pickup. =====

    [StringLength(120)]
    public string? RecipientName { get; set; }

    [StringLength(30)]
    public string? ContactNumber { get; set; }

    /// <summary>
    /// Must be one of the approved campus locations. This is what keeps delivery
    /// on campus - an arbitrary address cannot be stored.
    /// </summary>
    public int? CampusLocationId { get; set; }

    [StringLength(150)]
    public string? SpecificLocation { get; set; }

    [StringLength(500)]
    public string? DeliveryInstructions { get; set; }

    // ===== shared =====

    [StringLength(1000)]
    public string? Notes { get; set; }
}

public class UpdateOrderStatusRequest
{
    [Required]
    public string Status { get; set; } = "";
}

public class CancelMarketplaceOrderRequest
{
    [StringLength(500)]
    public string? Reason { get; set; }
}

// ------------------------------------------------------------
// Checkout info + settings
// ------------------------------------------------------------

/// <summary>
/// What checkout needs to render honestly: the real pickup location and
/// instructions, and the staff-configured delivery fee.
/// </summary>
public class MarketplaceCheckoutInfoDto
{
    public string PickupLocation { get; set; } = "";
    public string PickupInstructions { get; set; } = "";
    public bool DeliveryEnabled { get; set; }

    /// <summary>Configured by staff. May be 0 - free delivery is valid.</summary>
    public int DeliveryFeeCentavos { get; set; }

    /// <summary>Restated on every checkout so the restriction is never a surprise.</summary>
    public string DeliveryRestrictionNotice { get; set; } = "";
}

public class MarketplaceSettingsDto
{
    public string PickupLocation { get; set; } = "";
    public string PickupInstructions { get; set; } = "";
    public bool DeliveryEnabled { get; set; }
    public int DeliveryFeeCentavos { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SaveSettingsRequest
{
    [StringLength(200)]
    public string PickupLocation { get; set; } = "";

    [StringLength(500)]
    public string PickupInstructions { get; set; } = "";

    public bool DeliveryEnabled { get; set; }

    /// <summary>Clamped server-side. 0 means free campus delivery.</summary>
    [Range(0, 1000000)]
    public int DeliveryFeeCentavos { get; set; }
}

// ------------------------------------------------------------
// Marketplace Head - staff account management
//
// The Marketplace Head is created in the EXISTING Admin > Users "Add Head"
// screen, exactly like the Library Head and Guidance Head. From there the Head
// manages the marketplace's own operator accounts.
//
// NOTE ON WHO MAY DO THIS: this is NOT the CDM OneServe Admin. The OneServe Admin
// is read-only in the marketplace (spec 39) and has no endpoint that creates or
// edits a user. The Head is a module owner, and managing its module's staff is
// that role's job.
// ------------------------------------------------------------

/// <summary>One operator account on the Head's Staff Accounts screen.</summary>
public class MarketplaceStaffAccountDto
{
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string IdNumber { get; set; } = "";
    public string Email { get; set; } = "";

    /// <summary>Student | Faculty - the OneServe role the staff account keeps.</summary>
    public string AccountType { get; set; } = "";

    public string Course { get; set; } = "";
    public string Institute { get; set; } = "";
    public string ContactNumber { get; set; } = "";

    public bool IsActive { get; set; }
    public string AccountStatus { get; set; } = "";
    public DateTime AssignedAt { get; set; }

    /// <summary>The Head who assigned this account, when known.</summary>
    public string AssignedByName { get; set; } = "";

    /// <summary>How many orders this operator has handled. Handy when replacing one.</summary>
    public int OrdersHandled { get; set; }
}

/// <summary>
/// Creates a marketplace operator account.
///
/// This creates a normal OneServe user and then gives them the operator role -
/// it does not invent a second account system. The account can sign in with the
/// regular OneServe login.
/// </summary>
public class CreateMarketplaceStaffRequest
{
    [Required, StringLength(255, MinimumLength = 2)]
    public string FullName { get; set; } = "";

    [Required, EmailAddress, StringLength(255)]
    public string Email { get; set; } = "";

    /// <summary>The OneServe login the Head shares with the new operator.</summary>
    [Required, StringLength(120, MinimumLength = 8)]
    public string Password { get; set; } = "";

    /// <summary>Student or Faculty. Anything else is refused.</summary>
    [Required]
    public string AccountType { get; set; } = "Student";

    [StringLength(50)]
    public string? IdNumber { get; set; }

    [StringLength(100)]
    public string? Course { get; set; }

    [StringLength(150)]
    public string? Institute { get; set; }

    [StringLength(20)]
    public string? ContactNumber { get; set; }
}

// ------------------------------------------------------------
// Chat / Concerns
// ------------------------------------------------------------

public class MarketplaceConversationDto
{
    public int ConversationId { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string AccountType { get; set; } = "";
    public int? OrderId { get; set; }
    public string? OrderReference { get; set; }
    public string Status { get; set; } = "open";
    public string Subject { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int UnreadCount { get; set; }
    public string LastMessage { get; set; } = "";
    public DateTime? LastMessageAt { get; set; }
    public List<MarketplaceMessageDto> Messages { get; set; } = new();
}

public class MarketplaceMessageDto
{
    public int MessageId { get; set; }
    /// <summary>user | staff. The OneServe Admin is never `staff`.</summary>
    public string SenderType { get; set; } = "user";
    public int SenderId { get; set; }
    public string Message { get; set; } = "";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class StartConversationRequest
{
    /// <summary>Optional: links the thread to the order the question is about.</summary>
    public int? OrderId { get; set; }

    [StringLength(150)]
    public string Subject { get; set; } = "";

    [Required, StringLength(3000, MinimumLength = 1)]
    public string Message { get; set; } = "";
}

public class PostMessageRequest
{
    [Required, StringLength(3000, MinimumLength = 1)]
    public string Message { get; set; } = "";
}

public class UpdateConversationStatusRequest
{
    [Required]
    public string Status { get; set; } = "";
}

// ------------------------------------------------------------
// Staff dashboard + transactions
// ------------------------------------------------------------

/// <summary>Everything the single Staff Dashboard shows, in one read.</summary>
public class MarketplaceStaffDashboardDto
{
    // order counts by status
    public int PendingOrders { get; set; }
    public int ConfirmedOrders { get; set; }
    public int PreparingOrders { get; set; }
    public int ReadyForPickupOrders { get; set; }
    public int OutForDeliveryOrders { get; set; }
    public int DeliveredOrders { get; set; }
    public int CompletedOrders { get; set; }
    public int CancelledOrders { get; set; }

    // inventory
    public int TotalProducts { get; set; }
    public int LowStock { get; set; }
    public int OutOfStock { get; set; }

    // sales
    public long TodaySalesCentavos { get; set; }
    public long WeekSalesCentavos { get; set; }
    public long MonthSalesCentavos { get; set; }
    public long TotalSalesCentavos { get; set; }

    // fulfillment split
    public int PickupOrders { get; set; }
    public int CampusDeliveryOrders { get; set; }

    // chat
    public int OpenConversations { get; set; }
    public int UnreadMessages { get; set; }
}

/// <summary>One row in the Staff Transactions table.</summary>
public class MarketplaceTransactionDto
{
    public int OrderId { get; set; }
    public string OrderReference { get; set; } = "";
    public string BuyerName { get; set; } = "";
    /// <summary>Student | Faculty.</summary>
    public string AccountType { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string FulfillmentMethod { get; set; } = "";
    public string FulfillmentLabel { get; set; } = "";
    public int ItemCount { get; set; }
    public int TotalCentavos { get; set; }
    public string Status { get; set; } = "";
    public int? WorkspaceId { get; set; }
    public string WorkspaceName { get; set; } = "";
    public string? CampusLocationName { get; set; }
    public string? SpecificLocation { get; set; }
    public string? RecipientName { get; set; }
    public string? ContactNumber { get; set; }
    public string? DeliveryInstructions { get; set; }
    public List<MarketplaceOrderItemDto> Items { get; set; } = new();
}

// ------------------------------------------------------------
// Admin monitoring (READ-ONLY)
// ------------------------------------------------------------

/// <summary>
/// Marketplace information about a Student or Faculty account, for the CDM
/// OneServe Admin. Read-only by design: the Admin must not be able to edit or
/// delete these accounts through the marketplace (spec 39, 58).
/// </summary>
public class MarketplaceAccountMonitoringDto
{
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string IdNumber { get; set; } = "";
    public string Email { get; set; } = "";
    /// <summary>Student | Faculty.</summary>
    public string AccountType { get; set; } = "";
    public string Course { get; set; } = "";
    public string YearLevel { get; set; } = "";
    public string Institute { get; set; } = "";
    public int OrderCount { get; set; }
    public int CompletedOrderCount { get; set; }
    public long TotalPurchaseCentavos { get; set; }
    public DateTime? LastOrderAt { get; set; }
}

/// <summary>The Admin's read-only Marketplace overview.</summary>
public class MarketplaceMonitoringOverviewDto
{
    public long TotalSalesCentavos { get; set; }
    public long TodaySalesCentavos { get; set; }
    public long WeekSalesCentavos { get; set; }
    public long MonthSalesCentavos { get; set; }
    public int TotalOrders { get; set; }
    public int CompletedOrders { get; set; }
    public int CancelledOrders { get; set; }
    public int PendingOrders { get; set; }
    public int PickupOrders { get; set; }
    public int CampusDeliveryOrders { get; set; }
    public int StudentBuyers { get; set; }
    public int FacultyBuyers { get; set; }
}

/// <summary>Sales broken down for the Admin's analytics view.</summary>
public class MarketplaceSalesAnalyticsDto
{
    public long TotalSalesCentavos { get; set; }
    public List<MarketplaceSalesBucketDto> OverTime { get; set; } = new();
    public List<MarketplaceSalesGroupDto> ByCategory { get; set; } = new();
    public List<MarketplaceSalesGroupDto> ByAccountType { get; set; } = new();
    public List<MarketplaceBestSellerDto> BestSellers { get; set; } = new();
}

public class MarketplaceSalesBucketDto
{
    /// <summary>yyyy-MM-dd.</summary>
    public string Day { get; set; } = "";
    public long AmountCentavos { get; set; }
    public int Orders { get; set; }
}

public class MarketplaceSalesGroupDto
{
    public string Label { get; set; } = "";
    public long AmountCentavos { get; set; }
    public int Orders { get; set; }
}

public class MarketplaceBestSellerDto
{
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int UnitsSold { get; set; }
    public long AmountCentavos { get; set; }
}

// ------------------------------------------------------------
// Workspaces, stall accounts, operator sessions, audit
// ------------------------------------------------------------

/// <summary>A FoodHub location grouping stalls (NOT a delivery destination).</summary>
public class MarketplaceStallLocationDto
{
    public int StallLocationId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}

public class SaveStallLocationRequest
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = "";

    [StringLength(500)]
    public string Description { get; set; } = "";

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>One stall or the BusinessHub workspace.</summary>
public class MarketplaceWorkspaceDto
{
    public int WorkspaceId { get; set; }

    /// <summary>Stall | BusinessHub.</summary>
    public string WorkspaceType { get; set; } = "";
    public string Name { get; set; } = "";
    public int? StallLocationId { get; set; }
    public string StallLocationName { get; set; } = "";
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }

    /// <summary>True when this workspace already has a login account.</summary>
    public bool HasAccount { get; set; }
    public string AccountEmail { get; set; } = "";

    /// <summary>Designated operator named by the Head (empty when unset).</summary>
    public string OperatorName { get; set; } = "";
    public string IdLast3 { get; set; } = "";
}

public class SaveWorkspaceRequest
{
    public int? WorkspaceId { get; set; }

    /// <summary>Stall | BusinessHub. Only the Head sets this.</summary>
    [Required]
    public string WorkspaceType { get; set; } = "Stall";

    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = "";

    /// <summary>Required for Stall, optional site label for BusinessHub.</summary>
    public int? StallLocationId { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Creates the login account of one workspace (Head only).</summary>
public class CreateStallAccountRequest
{
    /// <summary>
    /// Which stall gets the login. The setup screen defaults to the card it
    /// was opened from, but the Head may point it at any workspace that has
    /// no active account yet.
    /// </summary>
    public int WorkspaceId { get; set; }

    [Required, EmailAddress, StringLength(190)]
    public string Email { get; set; } = "";

    [Required, StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = "";

    [Required, StringLength(120, MinimumLength = 2)]
    public string FullName { get; set; } = "";

    /// <summary>
    /// The designated human operator: shown on the setup screen with a
    /// reminder, and matched (not trusted blindly) when a shift starts.
    /// </summary>
    [Required, StringLength(120, MinimumLength = 2)]
    public string OperatorName { get; set; } = "";

    /// <summary>Last 3 digits of the operator's ID number.</summary>
    [Required, StringLength(3, MinimumLength = 3)]
    public string IdLast3 { get; set; } = "";
}

/// <summary>Names (or renames) the designated operator of a stall account.</summary>
public class SetStallOperatorRequest
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string OperatorName { get; set; } = "";

    [Required, StringLength(3, MinimumLength = 3)]
    public string IdLast3 { get; set; } = "";
}

public class ResetStallAccountPasswordRequest
{
    [Required, StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = "";
}

/// <summary>Opens a duty shift: the human name behind a stall login.</summary>
public class StartOperatorSessionRequest
{
    /// <summary>
    /// The real name of the operator on duty. Required, 2-120 chars.
    /// This is attribution, not a credential.
    /// </summary>
    [Required, StringLength(120, MinimumLength = 2)]
    public string OperatorName { get; set; } = "";

    /// <summary>
    /// Only for operators WITHOUT a fixed stall account (legacy personal
    /// staff, Head). Stall accounts operate their own workspace; sending
    /// another one here is rejected.
    /// </summary>
    public int? WorkspaceId { get; set; }
}

public class MarketplaceOperatorSessionDto
{
    public Guid SessionId { get; set; }
    public int WorkspaceId { get; set; }
    public string WorkspaceName { get; set; } = "";
    public string WorkspaceType { get; set; } = "";
    public string StallLocationName { get; set; } = "";
    public string OperatorName { get; set; } = "";

    /// <summary>Last 3 ID digits, when the name matched the designated operator.</summary>
    public string IdLast3 { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>One row of the inventory audit trail.</summary>
public class MarketplaceInventoryTransactionDto
{
    public int TransactionId { get; set; }
    public int? ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int? VariantId { get; set; }
    public string VariantName { get; set; } = "";
    public int WorkspaceId { get; set; }
    public string WorkspaceName { get; set; } = "";

    /// <summary>
    /// Null for a movement a background schedule caused - a system-initiated
    /// stock release has no human to attribute it to. See
    /// 011_system_audit_actor.sql.
    /// </summary>
    public int? ActorUserId { get; set; }

    public string ActorName { get; set; } = "";
    public string OperatorName { get; set; } = "";
    public string IdLast3 { get; set; } = "";
    public int PreviousQuantity { get; set; }
    public int QuantityChanged { get; set; }
    public int NewQuantity { get; set; }
    public string ActionType { get; set; } = "";
    public string Reason { get; set; } = "";
    public int? OrderId { get; set; }
    public string OrderReference { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
// ============================================================
// CAMPUS SERVICES DTOs
//
// Added by Database/Marketplace/003_campus_services.sql. Same conventions as
// the DTOs above: *Dto for reads, *Request for commands, money always integer
// *_Centavos, and nothing here carries a password or writes to the shared user.
//
// Sections: campus card -> ID claim -> pre-order checkout + claim verification
// ============================================================

// ------------------------------------------------------------
// CAMPUS CARD (digital payment + balance)
// ------------------------------------------------------------

/// <summary>
/// The buyer's campus card. Issued lazily on first read, so a student who never
/// touches the card never gets a row.
/// </summary>
public class CampusCardDto
{
    public int CardId { get; set; }
    public int UserId { get; set; }

    /// <summary>Owner name from OneServe - the card has no name of its own.</summary>
    public string FullName { get; set; } = "";
    public string IdNumber { get; set; } = "";
    /// <summary>Student | Faculty, read from OneServe.</summary>
    public string AccountType { get; set; } = "";

    public string CardNumber { get; set; } = "";
    public int BalanceCentavos { get; set; }

    /// <summary>Active | Blocked.</summary>
    public string Status { get; set; } = "";

    /// <summary>True when this card can pay right now.</summary>
    public bool CanPay => Status == CampusCardStatus.Active;

    public DateTime IssuedAt { get; set; }
    public DateTime? LastLoadedAt { get; set; }

    /// <summary>The most recent ledger movements, newest first.</summary>
    public List<CampusCardTransactionDto> RecentTransactions { get; set; } = new();
}

public class CampusCardTransactionDto
{
    public long TransactionId { get; set; }

    /// <summary>Credit | Debit | Refund.</summary>
    public string Type { get; set; } = "";
    public string TypeLabel { get; set; } = "";

    /// <summary>Always positive; the sign comes from Type.</summary>
    public int AmountCentavos { get; set; }

    /// <summary>
    /// The same amount, signed for display: negative for Debit, positive for
    /// Credit and Refund. Saves the frontend from re-deriving the direction.
    /// </summary>
    public int SignedAmountCentavos { get; set; }

    public int BalanceAfterCentavos { get; set; }

    public int? ReferenceOrderId { get; set; }
    public string OrderReference { get; set; } = "";

    public string PerformedByName { get; set; } = "";
    public string OperatorName { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class LoadCardRequest
{
    /// <summary>How much to add. Integer centavos - never a peso float.</summary>
    [Range(1, 500_000)]
    public int AmountCentavos { get; set; }

    [StringLength(300)]
    public string? Notes { get; set; }
}

/// <summary>Set a card's Active/Blocked state. Blocking keeps the balance.</summary>
public class SetCardStatusRequest
{
    /// <summary>Active | Blocked.</summary>
    [Required]
    [StringLength(16)]
    public string Status { get; set; } = CampusCardStatus.Active;
}

// ------------------------------------------------------------
// STUDENT / FACULTY ID CLAIM
// ------------------------------------------------------------

public class IdClaimDto
{
    public int IdClaimId { get; set; }
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string IdNumber { get; set; } = "";
    public string AccountType { get; set; } = "";
    public string Email { get; set; } = "";

    /// <summary>FirstIssue | Replacement | Lost | Damaged.</summary>
    public string ClaimType { get; set; } = "";
    public string ClaimTypeLabel { get; set; } = "";

    public string Reason { get; set; } = "";

    /// <summary>Pending | ReadyForClaim | Claimed | Rejected.</summary>
    public string Status { get; set; } = "";

    public string? ClaimCode { get; set; }
    public string? ReviewNotes { get; set; }
    public string ReviewedByName { get; set; } = "";

    public DateTime? ReviewedAt { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// The status staff may set next by approving, or null when the claim is
    /// finished. Computed server-side so the staff UI never offers an illegal
    /// move.
    /// </summary>
    public string? NextStatus { get; set; }

    /// <summary>
    /// True only while a Pending claim can still be turned down. Once it is
    /// released the answer is to hand the ID over, not to reject it.
    /// </summary>
    public bool CanReject { get; set; }

    /// <summary>True when the next approve releases it for collection (not hands it over).</summary>
    public bool IsAwaitingRelease { get; set; }
}

public class RequestIdClaimRequest
{
    /// <summary>FirstIssue | Replacement | Lost | Damaged.</summary>
    [Required]
    [StringLength(16)]
    public string ClaimType { get; set; } = IdClaimType.Replacement;

    /// <summary>Required by the service when ClaimType is Lost or Damaged.</summary>
    [StringLength(500)]
    public string? Reason { get; set; }
}

/// <summary>Staff decision on a claim. Approve releases it for collection.</summary>
public class ReviewIdClaimRequest
{
    /// <summary>
    /// Approve a Pending claim for collection, or reject it. On a claim that is
    /// already ReadyForClaim, approving marks it collected - so the staff screen
    /// needs only one toggle.
    /// </summary>
    public bool Approve { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}

// ------------------------------------------------------------
// PRE-ORDER CHECKOUT + CLAIM VERIFICATION
// ------------------------------------------------------------

/// <summary>
/// What the canteen/paper shop needs to offer pre-ordering, so the buyer screen
/// does not have to hardcode which day of the week it is.
/// </summary>
public class PreOrderOptionsDto
{
    public bool Enabled { get; set; }

    /// <summary>The soonest slot that can be booked, UTC.</summary>
    public DateTime EarliestSlotUtc { get; set; }

    /// <summary>The furthest slot that can be booked, UTC.</summary>
    public DateTime LatestSlotUtc { get; set; }

    /// <summary>Step between bookable slots, in minutes.</summary>
    public int SlotStepMinutes { get; set; }
}

/// <summary>
/// The counter lookup. Staff type the code the buyer shows and get back the
/// order to release - never the whole order table to sift through.
/// </summary>
public class ClaimLookupDto
{
    /// <summary>True when the code matched something collectable right now.</summary>
    public bool Found { get; set; }

    /// <summary>Why it did not match, phrased for the person at the counter.</summary>
    public string Message { get; set; } = "";

    /// <summary>The order, when Found.</summary>
    public MarketplaceOrderDto? Order { get; set; }
}

/// <summary>Releases a claimed order at the counter.</summary>
public class VerifyClaimRequest
{
    /// <summary>The claim code the buyer showed.</summary>
    [Required]
    [StringLength(8)]
    public string ClaimCode { get; set; } = "";

    [StringLength(300)]
    public string? Notes { get; set; }
}

// ============================================================
// STOREFRONT DTOs
//
// Created by Database/Marketplace/006_storefronts.sql. *Dto for reads,
// *Request for commands. Money stays integer *_Centavos.
// ============================================================

/// <summary>A storefront as a buyer sees it in the browse list.</summary>
public class MarketplaceStoreDto
{
    public int StoreId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Kind { get; set; } = "";
    public string KindLabel { get; set; } = "";
    public string Location { get; set; } = "";
    public string? PhotoUrl { get; set; }
    public string? BannerUrl { get; set; }

    /// <summary>active | paused | banned | deleted.</summary>
    public string Status { get; set; } = "";

    /// <summary>True when a buyer may place an order here right now.</summary>
    public bool IsOrderable { get; set; } = true;

    public bool DeliveryEnabled { get; set; }
    public int DeliveryFeeCentavos { get; set; }

    /// <summary>The fulfilment workspace behind the storefront.</summary>
    public int? WorkspaceId { get; set; }

    /// <summary>That workspace's name, for the Head's management screen.</summary>
    public string WorkspaceName { get; set; } = "";

    // ===== owner, for the management screens =====

    public int? OwnerUserId { get; set; }
    public string OwnerName { get; set; } = "";
    public string OwnerEmail { get; set; } = "";

    /// <summary>True when the caller owns this store.</summary>
    public bool IsOwner { get; set; }

    /// <summary>
    /// True when the caller is a team member with every permission - i.e. they
    /// can use the store workspace as if they owned it.
    /// </summary>
    public bool CanManage { get; set; }

    /// <summary>The caller's granted permissions. Empty for a buyer.</summary>
    public List<string> MyPermissions { get; set; } = new();

    // ===== public counters =====

    public int ProductCount { get; set; }

    /// <summary>
    /// Units sold from completed, paid orders - the "popular" sort key, matching
    /// the CampusMarket rule that unpaid or cancelled sales never count.
    /// </summary>
    public int SoldUnits { get; set; }

    /// <summary>Sales in centavos from completed, paid orders.</summary>
    public int SalesCentavos { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Sales figures for one storefront, for the owner or a member holding the
/// "analytics" permission. Created by Database/Marketplace/018_store_analytics.sql.
///
/// Read-only and derived - never stored. The daily series comes from the
/// marketplace_sales_daily view, which counts completed AND paid orders only.
/// </summary>
public class MarketplaceStoreAnalyticsDto
{
    public int StoreId { get; set; }

    /// <summary>Paid sales across the whole 28-day window, in centavos.</summary>
    public long Sales28dCentavos { get; set; }

    /// <summary>Orders contributing to Sales28dCentavos.</summary>
    public int Orders28d { get; set; }

    /// <summary>Units sold across the whole 28-day window.</summary>
    public int Units28d { get; set; }

    /// <summary>
    /// Average daily sales over the previous seven COMPLETE Manila days, in
    /// centavos. Days with no sale count as zero.
    /// </summary>
    public long DailyAverageCentavos { get; set; }

    /// <summary>
    /// Plain estimate for the next seven days: the sum of the previous seven
    /// complete days. An average, explicitly not a model.
    /// </summary>
    public long Forecast7dCentavos { get; set; }

    /// <summary>How many of the previous seven complete days had any paid sale.</summary>
    public int DaysWithSales { get; set; }

    /// <summary>
    /// One row per Manila calendar day for the last 28 days, oldest first, with
    /// quiet days present as zero so the chart has no gaps.
    /// </summary>
    public List<MarketplaceSalesBucketDto> SalesDaily { get; set; } = new();
}

/// <summary>
/// One team member, with their permissions.
///
/// The owner is included here as a synthetic row (ownerUserId set, isOwner
/// true) so the team screen shows them instead of a confusing gap.
/// </summary>
public class MarketplaceStoreMemberDto
{
    public int StoreId { get; set; }
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string IdNumber { get; set; } = "";
    public string Email { get; set; } = "";

    /// <summary>Free text chosen by the owner.</summary>
    public string Position { get; set; } = "";

    public bool IsOwner { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>The five permission names currently granted.</summary>
    public List<string> Permissions { get; set; } = new();

    public DateTime AssignedAt { get; set; }
}

/// <summary>A request to join a store's team.</summary>
public class MarketplaceStoreApplicationDto
{
    public int ApplicationId { get; set; }
    public int StoreId { get; set; }
    public string StoreName { get; set; } = "";
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string IdNumber { get; set; } = "";
    public string Email { get; set; } = "";
    public string Position { get; set; } = "";
    public string Message { get; set; } = "";

    /// <summary>pending | approved | rejected.</summary>
    public string Status { get; set; } = "";

    public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

/// <summary>Create or edit a storefront. Used by the Marketplace Head.</summary>
public class SaveMarketplaceStoreRequest
{
    /// <summary>Null creates; set edits.</summary>
    public int? StoreId { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = "";

    [StringLength(3000)]
    public string? Description { get; set; }

    /// <summary>food | supplies.</summary>
    [StringLength(16)]
    public string Kind { get; set; } = MarketplaceStoreKind.Food;

    [StringLength(200)]
    public string? Location { get; set; }

    [StringLength(255)]
    public string? PhotoUrl { get; set; }

    [StringLength(255)]
    public string? BannerUrl { get; set; }

    /// <summary>Which owner account fronts this store. Head-only.</summary>
    public int? OwnerUserId { get; set; }

    /// <summary>The fulfilment workspace to bridge to. Head-only.</summary>
    public int? WorkspaceId { get; set; }

    public bool DeliveryEnabled { get; set; }

    [Range(0, 100_000)]
    public int DeliveryFeeCentavos { get; set; }

    /// <summary>active | paused. Banned and deleted are not settable here.</summary>
    [StringLength(16)]
    public string Status { get; set; } = MarketplaceStoreStatus.Active;
}

/// <summary>Add or edit a team member, or remove them when IsActive is false.</summary>
public class SaveStoreMemberRequest
{
    public int UserId { get; set; }

    [StringLength(120)]
    public string? Position { get; set; }

    /// <summary>
    /// Any subset of catalog/orders/cash/analytics/delivery. Unknown values are
    /// dropped; an empty list falls back to the default so a member is never
    /// created with no permissions and no idea why.
    /// </summary>
    public List<string>? Permissions { get; set; }

    /// <summary>False deactivates the member instead of updating them.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>The owner's decision on a job application.</summary>
public class ReviewStoreApplicationRequest
{
    public bool Approve { get; set; }

    /// <summary>
    /// The position actually granted. Required when approving, because the
    /// applicant asked for one thing and the owner may decide otherwise.
    /// </summary>
    [StringLength(120)]
    public string? Position { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    /// <summary>Permissions to grant on approval. Empty falls back to the default.</summary>
    public List<string>? Permissions { get; set; }
}

/// <summary>Apply to join a store's team.</summary>
public class ApplyToStoreRequest
{
    public int StoreId { get; set; }

    [Required]
    [StringLength(120)]
    public string Position { get; set; } = "";

    [StringLength(1000)]
    public string? Message { get; set; }
}

/// <summary>A verified Student or Faculty account applies to become a seller.</summary>
public class ApplyToSellRequest
{
    [Required]
    [StringLength(100)]
    public string BusinessName { get; set; } = "";

    [StringLength(3000)]
    public string? Description { get; set; }

    /// <summary>
    /// Optional private document path. Uploaded to the identity-docs bucket,
    /// the same way the physical ID is uploaded, and must start with the
    /// applicant's own user id.
    /// </summary>
    [StringLength(500)]
    public string? DocumentPath { get; set; }
}

/// <summary>The Head's decision on a seller application.</summary>
public class ReviewSellerApplicationRequest
{
    public bool Approve { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

/// <summary>A seller application as the Head sees it.</summary>
public class MarketplaceSellerApplicationDto
{
    public int ApplicationId { get; set; }
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";

    public string BusinessName { get; set; } = "";
    public string Description { get; set; } = "";
    public string? DocumentPath { get; set; }

    /// <summary>pending | approved | rejected.</summary>
    public string Status { get; set; } = "";

    public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

/// <summary>A storefront category. StoreId null means system-wide.</summary>
public class MarketplaceCategoryDto
{
    public int CategoryId { get; set; }

    /// <summary>Null = a system category, valid for any store.</summary>
    public int? StoreId { get; set; }

    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public int ProductCount { get; set; }
}

/// <summary>Create or rename a storefront category.</summary>
public class SaveStoreCategoryRequest
{
    public int? CategoryId { get; set; }

    [Required]
    [StringLength(80)]
    public string Name { get; set; } = "";

    public int SortOrder { get; set; }
}

/// <summary>
/// One gallery image for a product.
///
/// Max eight per product and HTTPS-only, matching the CampusMarket rules. The
/// existing products.ImageUrl remains the primary image.
/// </summary>
public class ProductPhotoDto
{
    public int PhotoId { get; set; }
    public int ProductId { get; set; }
    public string Url { get; set; } = "";
    public int SortOrder { get; set; }
}

public class SaveProductPhotoRequest
{
    [Required]
    [StringLength(2048)]
    public string Url { get; set; } = "";

    public int SortOrder { get; set; }
}

/// <summary>A buyer's review of a storefront.</summary>
public class MarketplaceReviewDto
{
    public int ReviewId { get; set; }

    /// <summary>The order this review is about. Null once it is deleted.</summary>
    public int? OrderId { get; set; }

    public int? UserId { get; set; }

    public int StoreId { get; set; }

    /// <summary>1-5.</summary>
    public int Rating { get; set; }

    public string Body { get; set; } = "";

    public List<ReviewPhotoDto> Photos { get; set; } = new();

    public DateTime CreatedAt { get; set; }
}

public class ReviewPhotoDto
{
    public int PhotoId { get; set; }
    public int ReviewId { get; set; }
    public string Url { get; set; } = "";
    public int SortOrder { get; set; }
}

/// <summary>Rating summary for a store. Null rating means no reviews yet.</summary>
public class MarketplaceStoreRatingDto
{
    public int StoreId { get; set; }

    /// <summary>Average rating rounded to one decimal, or null when there are none.</summary>
    public double? Rating { get; set; }

    public int ReviewCount { get; set; }
}

public class ReviewOrderRequest
{
    public int OrderId { get; set; }

    [Range(1, 5)]
    public int Rating { get; set; }

    [StringLength(2000)]
    public string? Body { get; set; }

    /// <summary>HTTPS URLs, at most 5. Unknown or non-HTTPS entries are dropped.</summary>
    public List<string>? Photos { get; set; }
}

/// <summary>A buyer's request to return a paid order.</summary>
public class RequestReturnRequest
{
    public int OrderId { get; set; }

    [Required]
    [StringLength(2000)]
    public string Reason { get; set; } = "";

    /// <summary>When approved and confirmed, the stock goes back on the shelf.</summary>
    public bool Restock { get; set; } = true;
}

/// <summary>The owner's or administrator's decision on a refund request.</summary>
public class ReviewRefundRequest
{
    public bool Approve { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}

/// <summary>A refund request as it appears in the queue.</summary>
public class MarketplaceRefundRequestDto
{
    public int OrderId { get; set; }

    public string Reason { get; set; } = "";

    /// <summary>pending | approved | rejected | processing | completed.</summary>
    public string Status { get; set; } = "";

    public bool Restock { get; set; }

    public string? ReviewNote { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

// ============================================================
// MODERATION DTOs
// ============================================================

public class MarketplaceBlockDto
{
    public int BlockId { get; set; }
    public int UserId { get; set; }
    public int? TargetUserId { get; set; }
    public int? TargetStoreId { get; set; }
    public string TargetName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class MarketplaceReportDto
{
    public int ReportId { get; set; }
    public int ReporterId { get; set; }
    public string ReporterName { get; set; } = "";
    public int? TargetUserId { get; set; }
    public string TargetUserName { get; set; } = "";
    public int? TargetStoreId { get; set; }
    public string TargetStoreName { get; set; } = "";
    public string Reason { get; set; } = "";

    /// <summary>open | reviewed | dismissed.</summary>
    public string Status { get; set; } = "";

    public string? Resolution { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

public class MarketplaceSupportTicketDto
{
    public int TicketId { get; set; }
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";

    /// <summary>open | answered | closed.</summary>
    public string Status { get; set; } = "";

    public string? Response { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

public class BlockRequest
{
    /// <summary>Block a user. Mutually exclusive with TargetStoreId.</summary>
    public int? TargetUserId { get; set; }

    /// <summary>Block a store. Mutually exclusive with TargetUserId.</summary>
    public int? TargetStoreId { get; set; }
}

public class ReportRequest
{
    public int? TargetUserId { get; set; }
    public int? TargetStoreId { get; set; }

    [Required]
    [StringLength(2000, MinimumLength = 5)]
    public string Reason { get; set; } = "";
}

public class ResolveReportRequest
{
    /// <summary>reviewed or dismissed.</summary>
    [Required]
    [StringLength(16)]
    public string Status { get; set; } = "";

    [StringLength(2000)]
    public string? Resolution { get; set; }
}

public class OpenTicketRequest
{
    [Required]
    [StringLength(150, MinimumLength = 3)]
    public string Subject { get; set; } = "";

    [Required]
    [StringLength(3000, MinimumLength = 5)]
    public string Body { get; set; } = "";
}

public class RespondTicketRequest
{
    [Required]
    [StringLength(3000, MinimumLength = 1)]
    public string Response { get; set; } = "";
}

public class SetBanRequest
{
    public bool Banned { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }
}
