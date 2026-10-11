using System.ComponentModel.DataAnnotations;

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
    public int ActorUserId { get; set; }
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