using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// The BUYER side of CampusMarket - browsing and the cart.
///
///   GET  /api/marketplace/me
///   GET  /api/marketplace/checkout-info
///   GET  /api/marketplace/campus-locations
///   GET  /api/marketplace/products
///   GET  /api/marketplace/products/{id}
///   GET  /api/marketplace/cart
///   POST /api/marketplace/cart/items
///   PUT  /api/marketplace/cart/items/{cartItemId}
///   DELETE /api/marketplace/cart
///
/// [Authorize] here means the EXISTING OneServe JWT. There is no marketplace
/// login and no marketplace register - the marketplace has no accounts of its own.
/// </summary>
[ApiController]
[Route("api/marketplace")]
[Authorize]
public class MarketplaceCatalogController : MarketplaceControllerBase
{
    private readonly MarketplaceCatalogService _catalog;

    public MarketplaceCatalogController(MarketplaceCatalogService catalog)
    {
        _catalog = catalog;
    }

    /// <summary>
    /// The signed-in buyer, read from the OneServe account. Used by the frontend
    /// to show the name and to confirm the account may buy at all.
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _catalog.GetBuyerAsync(userId, ct));
    }

    /// <summary>
    /// Pickup location and instructions plus the staff-configured campus delivery
    /// fee. Fetched before checkout so the buyer never sees a surprise fee.
    /// </summary>
    [HttpGet("checkout-info")]
    public async Task<IActionResult> CheckoutInfo(CancellationToken ct) =>
        ToAction(await _catalog.GetCheckoutInfoAsync(ct));

    /// <summary>
    /// The approved campus delivery destinations. This is the ONLY set of places
    /// a Campus Delivery order can go - there is no address field anywhere.
    /// </summary>
    [HttpGet("campus-locations")]
    public async Task<IActionResult> CampusLocations(CancellationToken ct) =>
        ToAction(await _catalog.ListCampusLocationsAsync(ct));

    [HttpGet("products")]
    public async Task<IActionResult> Products(
        [FromQuery] string? category,
        [FromQuery] string? subCategory,
        [FromQuery] string? search,
        [FromQuery] int? workspaceId,
        CancellationToken ct) =>
        ToAction(await _catalog.ListProductsAsync(category, subCategory, search, workspaceId, ct));

    [HttpGet("products/{productId:int}")]
    public async Task<IActionResult> Product(int productId, CancellationToken ct) =>
        ToAction(await _catalog.GetProductAsync(productId, ct));

    /// <summary>
    /// The stalls and the BusinessHub a buyer may shop from, so the shop can
    /// group the catalog by FoodHub location instead of one flat list.
    /// </summary>
    [HttpGet("workspaces")]
    public async Task<IActionResult> Workspaces(CancellationToken ct) =>
        ToAction(await _catalog.ListWorkspacesAsync(ct));

    /// <summary>Active FoodHub locations, for grouping stalls while browsing.</summary>
    [HttpGet("stall-locations")]
    public async Task<IActionResult> StallLocations(CancellationToken ct) =>
        ToAction(await _catalog.ListStallLocationsAsync(ct));

    // ----- cart -----

    [HttpGet("cart")]
    public async Task<IActionResult> Cart(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _catalog.GetCartAsync(userId, ct));
    }

    /// <summary>
    /// Adds a line, or raises the quantity if that exact product+size is already
    /// in the cart. Never more than the shelf holds.
    /// </summary>
    [HttpPost("cart/items")]
    public async Task<IActionResult> AddToCart(
        [FromBody] AddCartItemRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _catalog.AddToCartAsync(
            userId, request.ProductId, request.VariantId, request.Quantity, ct));
    }

    /// <summary>Sets an exact quantity. 0 removes the line.</summary>
    [HttpPut("cart/items/{cartItemId:int}")]
    public async Task<IActionResult> SetCartQuantity(
        int cartItemId,
        [FromBody] SetCartQuantityRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _catalog.SetCartQuantityAsync(
            userId, cartItemId, request.Quantity, ct));
    }

    [HttpDelete("cart")]
    public async Task<IActionResult> ClearCart(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _catalog.ClearCartAsync(userId, ct));
    }
}