using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// The buyer's own orders. Pick Up and Campus Delivery share this one controller -
/// they are two fulfillment methods of the same ordering system.
///
///   POST   /api/marketplace/orders
///   GET    /api/marketplace/orders
///   GET    /api/marketplace/orders/{orderId}
///   POST   /api/marketplace/orders/{orderId}/cancel
///
/// Every read is scoped to the signed-in buyer in the query itself
/// (UserId == the JWT subject), so another buyer's order id returns 404 rather
/// than their data. The staff list is on a different controller entirely.
/// </summary>
[ApiController]
[Route("api/marketplace/orders")]
[Authorize]
public class MarketplaceOrdersController : MarketplaceControllerBase
{
    private readonly MarketplaceOrderService _orders;

    public MarketplaceOrdersController(MarketplaceOrderService orders)
    {
        _orders = orders;
    }

    /// <summary>
    /// Places the cart as one order and reserves the stock. Prices and the
    /// delivery fee are re-derived server-side; nothing money-related is taken
    /// from the request.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> PlaceOrder(
        [FromBody] PlaceMarketplaceOrderRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _orders.PlaceOrderAsync(userId, request, ct));
    }

    /// <summary>The buyer's own orders, newest first. Includes cancelled ones.</summary>
    [HttpGet]
    public async Task<IActionResult> MyOrders(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _orders.GetMyOrdersAsync(userId, ct));
    }

    /// <summary>
    /// One order with its items and timeline. Ownership is validated on the row,
    /// so this can only ever return the caller's own order.
    /// </summary>
    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> MyOrder(int orderId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _orders.GetMyOrderAsync(userId, orderId, ct));
    }

    /// <summary>
    /// Cancels an order while it is still cancellable, releasing the reservation
    /// so the units go straight back on the shelf.
    /// </summary>
    [HttpPost("{orderId:int}/cancel")]
    public async Task<IActionResult> CancelOrder(
        int orderId,
        [FromBody] CancelMarketplaceOrderRequest? request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _orders.CancelMyOrderAsync(
            userId, orderId, request ?? new CancelMarketplaceOrderRequest(), ct));
    }
}