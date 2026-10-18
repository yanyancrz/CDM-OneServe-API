using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Marketplace;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Refunds and returns: a buyer requests a return on a paid order, the store
/// owner or an administrator reviews it, and a cash refund is confirmed at the
/// counter.
///
/// Created by Database/Marketplace/014_refunds.sql.
///
/// NO ONLINE PAYMENTS in this system (by decision). The CampusMarket original
/// also had a PayMongo refund path (prepare_online_refund / attach_refund /
/// confirm_online_refund / reconcileRefund), which is NOT ported here. Only the
/// cash path is implemented, and it is gated on the `cash` permission of the
/// store that owns the order's workspace.
///
/// RULES (from the CampusMarket spec)
///   * buyer only, paid orders only;
///   * reason at least 5 characters;
///   * one request per order ever (PK on OrderId);
///   * approve keeps return_requested + refund_pending;
///   * decline sets payment back to paid and order back to completed/placed;
///   * a cash refund is confirmed at the counter, restocking if requested.
/// </summary>
public class MarketplaceRefundService
{
    private readonly AppDbContext _db;
    private readonly MarketplaceStoreAccessService _access;
    private readonly MarketplaceInventoryService _inventory;
    private readonly MarketplaceEmailOutboxService _outbox;

    public MarketplaceRefundService(
        AppDbContext db,
        MarketplaceStoreAccessService access,
        MarketplaceInventoryService inventory,
        MarketplaceEmailOutboxService outbox)
    {
        _db = db;
        _access = access;
        _inventory = inventory;
        _outbox = outbox;
    }

    // =====================================================
    // BUYER - request a return
    // =====================================================

    /// <summary>
    /// Requests a return/refund on a paid order. The order must belong to the
    /// caller and be paid. Refuses with a 409 when a request already exists.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceRefundRequestDto>> RequestAsync(
        int userId, int orderId, string reason, bool restock, CancellationToken ct = default)
    {
        var text = (reason ?? "").Trim();
        if (text.Length < 5)
            return MarketplaceResult<MarketplaceRefundRequestDto>.BadRequest(
                "Say why you are returning it (at least 5 characters).");

        var order = await _db.MarketplaceOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.OrderId == orderId && o.UserId == userId, ct);

        if (order is null)
            return MarketplaceResult<MarketplaceRefundRequestDto>.NotFound("That order does not exist.");

        if (order.PaymentStatus != "paid")
            return MarketplaceResult<MarketplaceRefundRequestDto>.BadRequest(
                "You can only request a return on a paid order.");

        if (order.Status is MarketplaceOrderStatus.Cancelled
            or MarketplaceOrderStatus.Expired
            or MarketplaceOrderStatus.Refunded)
            return MarketplaceResult<MarketplaceRefundRequestDto>.BadRequest(
                $"This order is {order.Status}, so it cannot be returned.");

        var already = await _db.MarketplaceRefundRequests.AsNoTracking()
            .AnyAsync(r => r.OrderId == orderId, ct);

        if (already)
            return MarketplaceResult<MarketplaceRefundRequestDto>.Conflict(
                "A refund request already exists for this order.");

        var request = new MarketplaceRefundRequest
        {
            OrderId = orderId,
            Reason = text,
            Status = MarketplaceRefundStatus.Pending,
            Restock = restock,
            CreatedAt = DateTime.UtcNow
        };

        _db.MarketplaceRefundRequests.Add(request);
        order.Status = MarketplaceOrderStatus.ReturnRequested;
        order.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The PK on OrderId fired: a concurrent double-tap won.
            _db.Entry(request).State = EntityState.Detached;
            return MarketplaceResult<MarketplaceRefundRequestDto>.Conflict(
                "A refund request already exists for this order.");
        }

        return MarketplaceResult<MarketplaceRefundRequestDto>.Created(
            ToDto(request), "Return request sent. The store will review it.");
    }

    // =====================================================
    // OWNER / ADMIN - review
    // =====================================================

    /// <summary>
    /// The owner's (or an administrator's) decision. Approving sets the order
    /// to refund_pending; declining restores paid + completed/placed.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceRefundRequestDto>> ReviewAsync(
        int userId, int orderId, bool approve, string? note, CancellationToken ct = default)
    {
        var request = await _db.MarketplaceRefundRequests
            .Include(r => r.Order)
            .FirstOrDefaultAsync(r => r.OrderId == orderId, ct);

        if (request is null)
            return MarketplaceResult<MarketplaceRefundRequestDto>.NotFound("That refund request does not exist.");

        if (request.Status != MarketplaceRefundStatus.Pending)
            return MarketplaceResult<MarketplaceRefundRequestDto>.BadRequest(
                $"That request is already {request.Status}.");

        var order = request.Order;
        if (order is null)
            return MarketplaceResult<MarketplaceRefundRequestDto>.NotFound("That order does not exist.");

        // Owner of the store that owns the workspace, OR a OneServe admin.
        // The store is resolved from the workspace, the same way the order's
        // workspace is resolved everywhere else in this module.
        var store = await _db.MarketplaceStores.AsNoTracking()
            .FirstOrDefaultAsync(s => s.WorkspaceId == order.WorkspaceId, ct);

        var isAdmin = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.AccountStatus == "Active"
                           && (u.Role == "Admin" || u.Role == "SuperAdmin"
                               || (u.Role == "MarketplaceAdmin" && u.AdminModule == "Marketplace")), ct);

        var isOwner = store is not null && store.OwnerUserId == userId;

        if (!isAdmin && !isOwner)
            return MarketplaceResult<MarketplaceRefundRequestDto>.Forbidden(
                "Only the store owner or an administrator can review a refund.");

        request.ReviewedBy = userId;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        if (!approve)
        {
            request.Status = MarketplaceRefundStatus.Rejected;
            order.PaymentStatus = "paid";
            order.Status = order.CompletedAt.HasValue
                ? MarketplaceOrderStatus.Completed
                : MarketplaceOrderStatus.Pending;
            order.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            return MarketplaceResult<MarketplaceRefundRequestDto>.Ok(
                ToDto(request), "Refund rejected. The order is restored.");
        }

        request.Status = MarketplaceRefundStatus.Approved;
        order.PaymentStatus = "refund_pending";
        order.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return MarketplaceResult<MarketplaceRefundRequestDto>.Ok(
            ToDto(request), "Refund approved. Confirm it at the counter to return the money.");
    }

    // =====================================================
    // OWNER / ADMIN - confirm a cash refund
    // =====================================================

    /// <summary>
    /// Confirms a cash refund at the counter: releases the stock if requested,
    /// marks the order refunded, and completes the request.
    ///
    /// The money side is deliberately a human step: it is the moment the cash
    /// is actually handed back, which is why there is no automatic version.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceRefundRequestDto>> ConfirmCashAsync(
        int userId, int orderId, CancellationToken ct = default)
    {
        var request = await _db.MarketplaceRefundRequests
            .Include(r => r.Order)
            .FirstOrDefaultAsync(r => r.OrderId == orderId, ct);

        if (request is null)
            return MarketplaceResult<MarketplaceRefundRequestDto>.NotFound("That refund request does not exist.");

        if (request.Status != MarketplaceRefundStatus.Approved)
            return MarketplaceResult<MarketplaceRefundRequestDto>.BadRequest(
                $"That request is {request.Status}, so it cannot be confirmed.");

        var order = request.Order;
        if (order is null)
            return MarketplaceResult<MarketplaceRefundRequestDto>.NotFound("That order does not exist.");

        var store = await _db.MarketplaceStores.AsNoTracking()
            .FirstOrDefaultAsync(s => s.WorkspaceId == order.WorkspaceId, ct);

        var isAdmin = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.AccountStatus == "Active"
                           && (u.Role == "Admin" || u.Role == "SuperAdmin"
                               || (u.Role == "MarketplaceAdmin" && u.AdminModule == "Marketplace")), ct);

        var isOwner = store is not null && store.OwnerUserId == userId;

        if (!isAdmin && !isOwner)
            return MarketplaceResult<MarketplaceRefundRequestDto>.Forbidden(
                "Only the store owner or an administrator can confirm a refund.");

        if (order.PaymentMethod != "cash")
            return MarketplaceResult<MarketplaceRefundRequestDto>.BadRequest(
                "Only cash refunds are confirmed here. Online refunds go through the provider.");

        // Stock is released through the same StockSettled lock as cancellation
        // and expiry, so a refund and a cancellation racing over the same order
        // cannot both release the same hold.
        if (request.Restock)
        {
            await _inventory.ReleaseOrderStockOnceAsync(
                order, userId, null, null, "Refund confirmed", "Refund", ct);
        }
        else
        {
            await _inventory.TryClaimStockSettleAsync(order.OrderId, ct);
        }

        order.Status = MarketplaceOrderStatus.Refunded;
        order.PaymentStatus = "refunded";
        order.CancelledAt = DateTime.UtcNow;
        order.UpdatedAt = DateTime.UtcNow;

        request.Status = MarketplaceRefundStatus.Completed;
        request.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _outbox.QueueRefundAsync(orderId, ct);

        return MarketplaceResult<MarketplaceRefundRequestDto>.Ok(
            ToDto(request), "Refund confirmed. The order is refunded.");
    }

    // =====================================================
    // READ
    // =====================================================

    /// <summary>One request, by order id.</summary>
    public async Task<MarketplaceResult<MarketplaceRefundRequestDto>> GetAsync(
        int orderId, CancellationToken ct = default)
    {
        var request = await _db.MarketplaceRefundRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.OrderId == orderId, ct);

        if (request is null)
            return MarketplaceResult<MarketplaceRefundRequestDto>.NotFound("No refund request for that order.");

        return MarketplaceResult<MarketplaceRefundRequestDto>.Ok(ToDto(request));
    }

    /// <summary>
    /// A store's refund requests. Owner or admin only.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceRefundRequestDto>>> ListForStoreAsync(
        int userId, int storeId, string? status, CancellationToken ct = default)
    {
        var store = await _db.MarketplaceStores.AsNoTracking()
            .FirstOrDefaultAsync(s => s.StoreId == storeId, ct);

        if (store is null)
            return MarketplaceResult<List<MarketplaceRefundRequestDto>>.NotFound("That store does not exist.");

        var isAdmin = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.AccountStatus == "Active"
                           && (u.Role == "Admin" || u.Role == "SuperAdmin"
                               || (u.Role == "MarketplaceAdmin" && u.AdminModule == "Marketplace")), ct);

        if (!isAdmin && store.OwnerUserId != userId)
            return MarketplaceResult<List<MarketplaceRefundRequestDto>>.Forbidden(
                "Only the store owner or an administrator can see refund requests.");

        var query = _db.MarketplaceRefundRequests.AsNoTracking()
            .Include(r => r.Order)
            .Where(r => r.Order!.WorkspaceId == store.WorkspaceId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = MarketplaceRefundStatus.Normalize(status);
            query = query.Where(r => r.Status == wanted);
        }

        var rows = await query
            .OrderBy(r => r.Status == MarketplaceRefundStatus.Pending ? 0 : 1)
            .ThenBy(r => r.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        return MarketplaceResult<List<MarketplaceRefundRequestDto>>.Ok(rows.Select(ToDto).ToList());
    }

    // =====================================================
    // INTERNAL
    // =====================================================

    private static MarketplaceRefundRequestDto ToDto(MarketplaceRefundRequest r) => new()
    {
        OrderId = r.OrderId,
        Reason = r.Reason,
        Status = r.Status,
        Restock = r.Restock,
        ReviewNote = r.ReviewNote,
        CreatedAt = r.CreatedAt,
        ReviewedAt = r.ReviewedAt,
        CompletedAt = r.CompletedAt
    };

    private const string StaffOnly = "Marketplace Staff access required.";
}
