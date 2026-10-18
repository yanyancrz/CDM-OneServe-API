using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// Refunds and returns: a buyer requests a return on a paid order, the store
/// owner or an administrator reviews it, and a cash refund is confirmed at the
/// counter.
///
/// Created by Database/Marketplace/014_refunds.sql.
///
/// [Authorize] is the plain OneServe JWT; the caller is always read from the
/// token, never from a body field.
/// </summary>
[ApiController]
[Route("api/marketplace/refunds")]
[Authorize]
public class MarketplaceRefundsController : MarketplaceControllerBase
{
    private readonly MarketplaceRefundService _refunds;

    public MarketplaceRefundsController(MarketplaceRefundService refunds) => _refunds = refunds;

    // =====================================================
    // BUYER - request a return
    // =====================================================

    /// <summary>
    /// Requests a return/refund on a paid order. Refuses with a 409 when a
    /// request already exists.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Request(
        [FromBody] RequestReturnRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _refunds.RequestAsync(
            userId, request.OrderId, request.Reason, request.Restock, ct));
    }

    /// <summary>One request, by order id.</summary>
    [HttpGet("orders/{orderId:int}")]
    public async Task<IActionResult> Get(int orderId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _refunds.GetAsync(orderId, ct));
    }

    // =====================================================
    // OWNER / ADMIN - review and confirm
    // =====================================================

    /// <summary>
    /// A store's refund requests. Owner or admin only. Filter by status.
    /// </summary>
    [HttpGet("stores/{storeId:int}")]
    public async Task<IActionResult> ForStore(
        int storeId,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _refunds.ListForStoreAsync(userId, storeId, status, ct));
    }

    /// <summary>
    /// The owner's or administrator's decision. Approving sets the order to
    /// refund_pending; declining restores paid + completed/placed.
    /// </summary>
    [HttpPost("orders/{orderId:int}/review")]
    public async Task<IActionResult> Review(
        int orderId,
        [FromBody] ReviewRefundRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _refunds.ReviewAsync(userId, orderId, request.Approve, request.Note, ct));
    }

    /// <summary>
    /// Confirms a cash refund at the counter: releases the stock if requested,
    /// marks the order refunded, and completes the request.
    /// </summary>
    [HttpPost("orders/{orderId:int}/confirm")]
    public async Task<IActionResult> Confirm(int orderId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _refunds.ConfirmCashAsync(userId, orderId, ct));
    }
}
