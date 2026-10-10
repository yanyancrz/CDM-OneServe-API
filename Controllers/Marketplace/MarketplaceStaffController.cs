using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// The single Marketplace Staff account's side of CampusMarket.
///
///   GET    /api/marketplace/staff/me
///   GET    /api/marketplace/staff/dashboard
///   GET    /api/marketplace/staff/orders
///   GET    /api/marketplace/staff/orders/{orderId}
///   POST   /api/marketplace/staff/orders/{orderId}/status
///   GET    /api/marketplace/staff/deliveries
///   GET    /api/marketplace/staff/transactions
///   GET    /api/marketplace/staff/products
///   POST   /api/marketplace/staff/products
///   PUT    /api/marketplace/staff/products/{productId}
///   POST   /api/marketplace/staff/products/{productId}/archive
///   POST   /api/marketplace/staff/products/{productId}/restore
///   GET    /api/marketplace/staff/inventory
///   GET    /api/marketplace/staff/inventory/low-stock
///   PUT    /api/marketplace/staff/inventory/{inventoryId}
///   POST   /api/marketplace/staff/inventory/{inventoryId}/restock
///   GET    /api/marketplace/staff/locations
///   POST   /api/marketplace/staff/locations
///   PUT    /api/marketplace/staff/locations/{locationId}
///   POST   /api/marketplace/staff/locations/{locationId}/archive
///   GET    /api/marketplace/staff/settings
///   PUT    /api/marketplace/staff/settings
///
/// [Authorize] is deliberately the plain OneServe JWT rather than a role string:
/// being Marketplace Staff is a row in marketplace_staff, not a OneServe role, so
/// the real check lives in MarketplaceAccessService. A Student/Faculty/Admin token
/// passes [Authorize] but is refused by every service method here with a 403.
/// </summary>
[ApiController]
[Route("api/marketplace/staff")]
[Authorize]
public class MarketplaceStaffController : MarketplaceControllerBase
{
    private readonly MarketplaceStaffDashboardService _dashboard;
    private readonly MarketplaceOrderService _orders;
    private readonly MarketplaceStaffCatalogService _catalog;
    private readonly MarketplaceInventoryService _inventory;
    private readonly MarketplaceAccessService _access;

    public MarketplaceStaffController(
        MarketplaceStaffDashboardService dashboard,
        MarketplaceOrderService orders,
        MarketplaceStaffCatalogService catalog,
        MarketplaceInventoryService inventory,
        MarketplaceAccessService access)
    {
        _dashboard = dashboard;
        _orders = orders;
        _catalog = catalog;
        _inventory = inventory;
        _access = access;
    }

    /// <summary>
    /// Confirms the caller may use the staff portal. The login page calls this
    /// after a successful OneServe login to decide where to land.
    ///
    /// Answers for BOTH marketplace roles: an operator (marketplace_staff) and
    /// the Marketplace Head. The Head is a superset of staff, so both belong here.
    ///
    /// It reports the marketplace's own view of the account; it never reveals
    /// anything a buyer could not already see about themselves.
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        var actor = await _access.GetOperatorAsync(userId, ct);
        if (actor is null)
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("This account is not a Marketplace Staff or Marketplace Head account."));

        return Ok(ApiResponse.Ok(new
        {
            userId = actor.UserId,
            fullName = actor.User.FullName,
            email = actor.User.Email,
            // Explicitly false: the OneServe Admin monitors, they do not operate.
            isAdmin = false,
            isStaff = actor.IsStaff,
            isHead = actor.IsHead,
            canOperate = actor.CanOperate,
            // Only the Head may manage operator accounts.
            canManageStaff = actor.IsHead
        }));
    }

    // ==========================================================
    // dashboard + transactions
    // ==========================================================

    /// <summary>Every number the Staff Dashboard shows, in one read.</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _dashboard.GetDashboardAsync(userId, ct));
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> Transactions(
        [FromQuery] string? status,
        [FromQuery] string? method,
        [FromQuery] string? search,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _dashboard.ListTransactionsAsync(
            userId, status, method, search, from, to, ct));
    }

    // ==========================================================
    // orders
    // ==========================================================

    /// <summary>
    /// All orders. Filter by status and fulfillment method to feed the Orders and
    /// Campus Deliveries screens from one endpoint.
    /// </summary>
    [HttpGet("orders")]
    public async Task<IActionResult> Orders(
        [FromQuery] string? status,
        [FromQuery] string? method,
        [FromQuery] string? search,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _orders.ListForStaffAsync(
            userId, status, method, search, from, to, ct));
    }

    /// <summary>Only Campus Delivery orders, for the Deliveries screen.</summary>
    [HttpGet("deliveries")]
    public async Task<IActionResult> Deliveries(
        [FromQuery] string? status,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _orders.ListDeliveriesAsync(userId, status, ct));
    }

    [HttpGet("orders/{orderId:int}")]
    public async Task<IActionResult> Order(int orderId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _orders.GetForStaffAsync(userId, orderId, ct));
    }

    /// <summary>
    /// Advances an order by one step along ITS OWN fulfillment path, or cancels it.
    ///
    /// The request may only name the step the server already considers next, so a
    /// Pick Up order can never be pushed down the delivery path, and staff cannot
    /// skip a step. Completing consumes the reserved stock.
    /// </summary>
    [HttpPost("orders/{orderId:int}/status")]
    public async Task<IActionResult> UpdateStatus(
        int orderId,
        [FromBody] UpdateOrderStatusRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _orders.UpdateStatusAsync(userId, orderId, request.Status, ct));
    }

    // ==========================================================
    // products
    // ==========================================================

    /// <summary>All products including retired ones, plus their sizes and stock.</summary>
    [HttpGet("products")]
    public async Task<IActionResult> Products(
        [FromQuery] string? category,
        [FromQuery] string? search,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _catalog.ListProductsAsync(userId, category, search, ct));
    }

    [HttpGet("products/{productId:int}")]
    public async Task<IActionResult> Product(int productId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _catalog.GetProductAsync(userId, productId, ct));
    }

    /// <summary>
    /// Creates or updates a product together with its size variants. The
    /// category/subcategory must be one of the spec's fixed lists, so the catalog
    /// cannot drift.
    /// </summary>
    [HttpPost("products")]
    public async Task<IActionResult> SaveProduct(
        [FromBody] SaveProductRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _catalog.SaveProductAsync(userId, request, ct));
    }

    /// <summary>
    /// Retires a product. It is deactivated, not deleted, so past order lines
    /// stay readable.
    /// </summary>
    [HttpPost("products/{productId:int}/archive")]
    public async Task<IActionResult> ArchiveProduct(int productId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _catalog.ArchiveProductAsync(userId, productId, ct));
    }

    [HttpPost("products/{productId:int}/restore")]
    public async Task<IActionResult> RestoreProduct(int productId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _catalog.RestoreProductAsync(userId, productId, ct));
    }

    // ==========================================================
    // inventory
    // ==========================================================

    /// <summary>
    /// Every inventory row with Current / Reserved / Available. Rows needing
    /// attention float to the top.
    /// </summary>
    [HttpGet("inventory")]
    public async Task<IActionResult> Inventory(
        [FromQuery] string? search,
        [FromQuery] string? stockState,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _inventory.ListAsync(userId, search, stockState, ct));
    }

    [HttpGet("inventory/low-stock")]
    public async Task<IActionResult> LowStock(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _inventory.GetLowStockAsync(userId, ct));
    }

    /// <summary>
    /// Sets the on-hand quantity and the low-stock threshold. Refused if it would
    /// drop below what open orders have already reserved.
    /// </summary>
    [HttpPut("inventory/{inventoryId:int}")]
    public async Task<IActionResult> SaveInventory(
        int inventoryId,
        [FromBody] SaveInventoryRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _inventory.SaveAsync(userId, inventoryId, request, ct));
    }

    /// <summary>
    /// Adds stock. Additive on purpose, so a miscount tops the shelf up instead
    /// of silently wiping it.
    /// </summary>
    [HttpPost("inventory/{inventoryId:int}/restock")]
    public async Task<IActionResult> Restock(
        int inventoryId,
        [FromBody] RestockInventoryRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _inventory.RestockAsync(userId, inventoryId, request.Quantity, ct));
    }

    // ==========================================================
    // campus locations
    // ==========================================================

    /// <summary>
    /// The approved campus delivery destinations, including retired ones. This
    /// list is the mechanism that keeps delivery on campus, so it is staff-managed.
    /// </summary>
    [HttpGet("locations")]
    public async Task<IActionResult> Locations(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _catalog.ListLocationsAsync(userId, ct));
    }

    [HttpPost("locations")]
    public async Task<IActionResult> SaveLocation(
        [FromBody] SaveCampusLocationRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _catalog.SaveLocationAsync(userId, null, request, ct));
    }

    [HttpPut("locations/{locationId:int}")]
    public async Task<IActionResult> SaveLocation(
        int locationId,
        [FromBody] SaveCampusLocationRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _catalog.SaveLocationAsync(userId, locationId, request, ct));
    }

    /// <summary>
    /// Retires a location. Existing orders keep the destination they were placed
    /// against; only new orders stop seeing it.
    /// </summary>
    [HttpPost("locations/{locationId:int}/archive")]
    public async Task<IActionResult> ArchiveLocation(int locationId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _catalog.ArchiveLocationAsync(userId, locationId, ct));
    }

    // ==========================================================
    // settings
    // ==========================================================

    /// <summary>
    /// Pickup location/instructions and the campus delivery fee. The fee is a
    /// staff setting and may legitimately be 0.
    /// </summary>
    [HttpGet("settings")]
    public async Task<IActionResult> Settings(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _catalog.GetSettingsAsync(userId, ct));
    }

    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings(
        [FromBody] SaveSettingsRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _catalog.SaveSettingsAsync(userId, request, ct));
    }
}