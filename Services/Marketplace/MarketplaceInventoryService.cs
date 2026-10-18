using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Models.Marketplace;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Inventory: the staff side of stock.
///
/// Two halves, and the split matters:
///
///   STAFF writes what is physically on the shelf (StockQuantity) and the
///   low-stock alert. Staff never touches ReservedQuantity.
///
///   ORDERS move ReservedQuantity. Placing a order reserves; cancelling
///   releases; completing consumes. Buyers can reach neither.
///
/// The oversell guard is the conditional UPDATE: it re-checks availability
/// inside the same statement that writes, so two buyers racing for the last
/// item cannot both win. The database CHECK is the second line of defence.
/// </summary>
/// <summary>
/// One stock movement's audit context, carried from the caller that owns the
/// surrounding workflow (staff screen, checkout, completion, cancellation).
/// </summary>
/// <summary>
/// One stock movement's audit context, carried from the caller that owns the
/// surrounding workflow (staff screen, checkout, completion, cancellation,
/// expiry).
///
/// ActorUserId is nullable: a movement caused by a background schedule has no
/// human to attribute it to, and that is a fact the audit row should record
/// rather than inventing a user id for. See 011_system_audit_actor.sql.
/// </summary>
public sealed record MarketplaceStockAudit(
    int WorkspaceId,
    int? ActorUserId,
    string? OperatorName,
    string? IdLast3,
    int? OrderId,
    string? Reason);

public class MarketplaceInventoryService
{
    private readonly AppDbContext _db;
    private readonly MarketplaceAccessService _access;
    private readonly MarketplaceOperatorSessionService _sessions;
    private readonly MarketplaceAuditService _audit;

    public MarketplaceInventoryService(
        AppDbContext db,
        MarketplaceAccessService access,
        MarketplaceOperatorSessionService sessions,
        MarketplaceAuditService audit)
    {
        _db = db;
        _access = access;
        _sessions = sessions;
        _audit = audit;
    }

    // ==========================================================
    // staff reads
    // ==========================================================

    /// <summary>Every inventory row, joined to its product and variant.</summary>
    public async Task<MarketplaceResult<List<MarketplaceInventoryDto>>> ListAsync(
        int staffId,
        string? search,
        string? stockState,
        Guid? sessionId,
        int? workspaceId,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<List<MarketplaceInventoryDto>>.Forbidden(StaffOnly);

        var (scope, scopeError) = await _sessions.ResolveReadScopeAsync(
            staffId, sessionId, workspaceId, ct);

        if (scopeError is not null)
            return MarketplaceResult<List<MarketplaceInventoryDto>>.Forbidden(scopeError);

        var rows = await _db.MarketplaceInventory.AsNoTracking()
            .Include(i => i.Product)
            .Include(i => i.Variant)
            .Where(i => scope == null || i.Product == null || i.Product.WorkspaceId == scope)
            .ToListAsync(ct);

        var dtos = rows.Select(ToDto).ToList();

        var needle = (search ?? "").Trim();
        if (needle.Length > 0)
        {
            dtos = dtos.Where(d =>
                d.ProductName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                d.VariantName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                d.Category.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(stockState))
        {
            var wanted = stockState.Trim().ToLowerInvariant();
            dtos = dtos.Where(d => d.StockState == wanted).ToList();
        }

        // Anything that needs attention floats to the top.
        return MarketplaceResult<List<MarketplaceInventoryDto>>.Ok(dtos
            .OrderBy(d => d.StockState switch
            {
                "out_of_stock" => 0,
                "low" => 1,
                _ => 2
            })
            .ThenByDescending(d => d.AvailableQuantity)
            .ToList());
    }

    /// <summary>Low stock - what staff should reorder first.</summary>
    public async Task<MarketplaceResult<List<MarketplaceInventoryDto>>> GetLowStockAsync(
        int staffId,
        Guid? sessionId,
        int? workspaceId,
        CancellationToken ct = default)
    {
        var all = await ListAsync(staffId, null, null, sessionId, workspaceId, ct);
        if (!all.Success) return all;

        return MarketplaceResult<List<MarketplaceInventoryDto>>.Ok(all.Data!
            .Where(d => d.StockState is "low" or "out_of_stock")
            .ToList());
    }

    // ==========================================================
    // staff writes
    // ==========================================================

    /// <summary>
    /// Sets the on-hand quantity for one product+variant. Reserved is left alone -
    /// it belongs to the orders, not to the shelf count.
    ///
    /// Requires an operator session: the entered operator name is what the
    /// audit row carries, and the row's workspace must be the session's.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceInventoryDto>> SaveAsync(
        int staffId,
        int inventoryId,
        SaveInventoryRequest request,
        Guid? sessionId,
        CancellationToken ct = default)
    {
        var (attribution, scopeError) = await _sessions.RequireMutationScopeAsync(
            staffId, sessionId, targetWorkspaceId: null, ct);

        if (attribution is null)
            return MarketplaceResult<MarketplaceInventoryDto>.Forbidden(
                scopeError ?? StaffOnly);

        var row = await _db.MarketplaceInventory
            .Include(i => i.Product)
            .FirstOrDefaultAsync(i => i.InventoryId == inventoryId, ct);

        if (row is null)
            return MarketplaceResult<MarketplaceInventoryDto>.NotFound("Inventory row not found.");

        if (row.Product?.WorkspaceId != attribution.WorkspaceId)
            return MarketplaceResult<MarketplaceInventoryDto>.Forbidden(
                "That belongs to another workspace.");

        if (request.StockQuantity < 0)
            return MarketplaceResult<MarketplaceInventoryDto>.BadRequest("Stock cannot be negative.");

        // Stock must stay at or above what is already reserved, otherwise the
        // shelf would promise less than the pending orders have claimed.
        if (request.StockQuantity < row.ReservedQuantity)
            return MarketplaceResult<MarketplaceInventoryDto>.BadRequest(
                $"{row.ReservedQuantity} unit(s) are reserved by open orders. " +
                "Cancel or complete those orders first.");

        var previous = row.StockQuantity;

        row.StockQuantity = request.StockQuantity;
        row.LowStockThreshold = Math.Max(0, request.LowStockThreshold);
        row.UpdatedAt = DateTime.UtcNow;

        _audit.Record(
            row.ProductId, row.VariantId, attribution.WorkspaceId, staffId,
            attribution.OperatorName, attribution.IdLast3,
            previous, row.StockQuantity - previous,
            "Adjustment", request.Reason);

        await _db.SaveChangesAsync(ct);

        var dto = await ReadAsync(staffId, inventoryId, ct);
        return MarketplaceResult<MarketplaceInventoryDto>.Ok(dto.Data!, "Inventory updated.");
    }

    /// <summary>
    /// Adds stock to a row. Used by staff restocking, and deliberately additive
    /// so a miscount tops up rather than wipes the shelf.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceInventoryDto>> RestockAsync(
        int staffId,
        int inventoryId,
        RestockInventoryRequest request,
        Guid? sessionId,
        CancellationToken ct = default)
    {
        var (attribution, scopeError) = await _sessions.RequireMutationScopeAsync(
            staffId, sessionId, targetWorkspaceId: null, ct);

        if (attribution is null)
            return MarketplaceResult<MarketplaceInventoryDto>.Forbidden(
                scopeError ?? StaffOnly);

        var quantity = request.Quantity;
        if (quantity <= 0)
            return MarketplaceResult<MarketplaceInventoryDto>.BadRequest("Enter a quantity to add.");

        var row = await _db.MarketplaceInventory
            .Include(i => i.Product)
            .FirstOrDefaultAsync(i => i.InventoryId == inventoryId, ct);

        if (row is null)
            return MarketplaceResult<MarketplaceInventoryDto>.NotFound("Inventory row not found.");

        if (row.Product?.WorkspaceId != attribution.WorkspaceId)
            return MarketplaceResult<MarketplaceInventoryDto>.Forbidden(
                "That belongs to another workspace.");

        var previous = row.StockQuantity;

        row.StockQuantity += quantity;
        row.UpdatedAt = DateTime.UtcNow;

        _audit.Record(
            row.ProductId, row.VariantId, attribution.WorkspaceId, staffId,
            attribution.OperatorName, attribution.IdLast3,
            previous, quantity,
            "Restock", request.Reason);

        await _db.SaveChangesAsync(ct);

        var dto = await ReadAsync(staffId, inventoryId, ct);
        return MarketplaceResult<MarketplaceInventoryDto>.Ok(dto.Data!, $"Added {quantity} unit(s).");
    }

    // ==========================================================
    // order-side movement (never called by a buyer)
    // ==========================================================

    /// <summary>
    /// Reserves stock for a new order.
    ///
    /// This is the oversell guard. The WHERE clause re-checks availability at
    /// write time, so if another buyer took the last unit between the read and
    /// the write, this returns 0 and the caller aborts. Two concurrent buyers
    /// cannot both succeed.
    /// </summary>
    public async Task<bool> TryReserveAsync(
        int productId,
        int? variantId,
        int quantity,
        MarketplaceStockAudit? audit,
        CancellationToken ct = default)
    {
        if (quantity <= 0) return true;

        var affected = await _db.MarketplaceInventory
            .Where(i => i.ProductId == productId &&
                        i.VariantId == variantId &&
                        (i.StockQuantity - i.ReservedQuantity) >= quantity)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.ReservedQuantity, i => i.ReservedQuantity + quantity)
                .SetProperty(i => i.UpdatedAt, DateTime.UtcNow), ct);

        if (affected != 1 || audit is null) return affected == 1;

        var stock = await _db.MarketplaceInventory.AsNoTracking()
            .Where(i => i.ProductId == productId && i.VariantId == variantId)
            .Select(i => i.StockQuantity)
            .FirstOrDefaultAsync(ct);

        _audit.Record(productId, variantId, audit.WorkspaceId, audit.ActorUserId,
            audit.OperatorName, audit.IdLast3, stock, quantity, "Reservation", audit.Reason, audit.OrderId);

        return true;
    }

    /// <summary>
    /// Claims an order's stock settle, returning true for exactly ONE caller.
    ///
    /// This is the idempotency lock for the whole order lifecycle, added by
    /// 010_order_lifecycle.sql.
    ///
    /// WHY IT HAS TO BE AT THE ORDER LEVEL
    ///   <see cref="ReleaseAsync"/> can only ask "does this SKU have a hold of at
    ///   least this much", because it does not know which order the hold belongs
    ///   to. That is enough to stop a negative count and not enough to stop a
    ///   double release: a second cancellation of an already-cancelled order
    ///   finds ANOTHER order's reservation on the same product and eats it,
    ///   starving a live order.
    ///
    ///   So the caller claims the order's StockSettled flag first, with a
    ///   conditional UPDATE. Only the caller whose UPDATE affected one row may
    ///   touch inventory. Two concurrent callers cannot both win a conditional
    ///   UPDATE, so this is a lock rather than a check - and it needs no explicit
    ///   transaction, because MySQL's row lock on the order row is held for the
    ///   duration of the UPDATE statement.
    ///
    /// Stock may legitimately be released, consumed, or left alone, so this only
    /// answers "is this still yours to act on", never "what should happen".
    /// </summary>
    public async Task<bool> TryClaimStockSettleAsync(int orderId, CancellationToken ct = default)
    {
        var affected = await _db.MarketplaceOrders
            .Where(o => o.OrderId == orderId && !o.StockSettled)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.StockSettled, true), ct);

        return affected == 1;
    }

    /// <summary>
    /// Gives back every hold on an order, exactly once.
    ///
    /// The order's StockSettled flag is claimed first; if somebody else already
    /// holds it this is a no-op, and the caller must then leave the order row
    /// alone too rather than writing a second cancellation over the first.
    /// </summary>
    public async Task<bool> ReleaseOrderStockOnceAsync(
        MarketplaceOrder order,
        int? actorUserId,
        string? operatorName,
        string? idLast3,
        string reason,
        string actionType = "Cancellation",
        CancellationToken ct = default)
    {
        if (!await TryClaimStockSettleAsync(order.OrderId, ct)) return false;

        await ReleaseOrderLinesAsync(order, actorUserId, operatorName, idLast3, reason, actionType, ct);
        return true;
    }

    /// <summary>
    /// The per-line release, with no idempotency of its own - the caller must
    /// have claimed the settle flag first.
    ///
    /// internal rather than public: it is an implementation detail, but the expiry
    /// job (MarketplaceOrderService) and the order service both need it, and they
    /// live in this assembly.
    /// </summary>
    internal async Task ReleaseOrderLinesAsync(
        MarketplaceOrder order,
        int? actorUserId,
        string? operatorName,
        string? idLast3,
        string reason,
        string actionType,
        CancellationToken ct)
    {
        foreach (var item in order.Items)
        {
            if (item.ProductId is not int productId) continue;

            // Post-migration every order carries its workspace; the lookup is
            // only a backstop so an audit row can never break a settle.
            var workspaceId = order.WorkspaceId ?? await _db.MarketplaceProducts
                .Where(p => p.ProductId == productId)
                .Select(p => p.WorkspaceId)
                .FirstOrDefaultAsync(ct) ?? 0;

            MarketplaceStockAudit? audit = workspaceId == 0 ? null : new MarketplaceStockAudit(
                workspaceId, actorUserId, operatorName, idLast3, order.OrderId, reason);

            await ReleaseAsync(productId, item.VariantId, item.Quantity, audit, ct, actionType);
        }
    }

    /// <summary>
    /// Releases a reservation - used when an order is cancelled or expires
    /// uncollected. The stock stays on the shelf; only the hold goes away.
    ///
    /// Deliberately NOT idempotent on its own: it cannot tell "release this
    /// order's hold" from "release any hold of this size", because it has no idea
    /// which order it is acting for. Callers acting on an order must go through
    /// <see cref="ReleaseOrderStockOnceAsync"/>, which claims the settle flag
    /// first.
    /// </summary>
    public async Task ReleaseAsync(
        int productId,
        int? variantId,
        int quantity,
        MarketplaceStockAudit? audit,
        CancellationToken ct = default,
        string actionType = "Cancellation")
    {
        if (quantity <= 0) return;

        var affected = await _db.MarketplaceInventory
            .Where(i => i.ProductId == productId &&
                        i.VariantId == variantId &&
                        i.ReservedQuantity >= quantity)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.ReservedQuantity, i => i.ReservedQuantity - quantity)
                .SetProperty(i => i.UpdatedAt, DateTime.UtcNow), ct);

        if (affected != 1 || audit is null) return;

        var stock = await _db.MarketplaceInventory.AsNoTracking()
            .Where(i => i.ProductId == productId && i.VariantId == variantId)
            .Select(i => i.StockQuantity)
            .FirstOrDefaultAsync(ct);

        _audit.Record(productId, variantId, audit.WorkspaceId, audit.ActorUserId,
            audit.OperatorName, audit.IdLast3, stock, -quantity, actionType, audit.Reason, audit.OrderId);
    }

    /// <summary>
    /// Consumes a reservation on completion: the unit leaves the shelf for good
    /// (Stock -1) and the hold is dropped (Reserved -1).
    /// </summary>
    public async Task ConsumeAsync(
        int productId,
        int? variantId,
        int quantity,
        MarketplaceStockAudit? audit,
        CancellationToken ct = default)
    {
        if (quantity <= 0) return;

        var affected = await _db.MarketplaceInventory
            .Where(i => i.ProductId == productId &&
                        i.VariantId == variantId &&
                        i.StockQuantity >= quantity)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.StockQuantity, i => i.StockQuantity - quantity)
                .SetProperty(i => i.UpdatedAt, DateTime.UtcNow), ct);

        if (affected == 1)
        {
            var stock = await _db.MarketplaceInventory.AsNoTracking()
                .Where(i => i.ProductId == productId && i.VariantId == variantId)
                .Select(i => i.StockQuantity)
                .FirstOrDefaultAsync(ct);

            if (audit is not null)
                _audit.Record(productId, variantId, audit.WorkspaceId, audit.ActorUserId,
                    audit.OperatorName, audit.IdLast3, stock + quantity, -quantity, "Sale", audit.Reason, audit.OrderId);
        }

        // Drop the hold separately, clamped so it can never go negative.
        await _db.MarketplaceInventory
            .Where(i => i.ProductId == productId &&
                        i.VariantId == variantId &&
                        i.ReservedQuantity > 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.ReservedQuantity,
                    i => i.ReservedQuantity > quantity ? i.ReservedQuantity - quantity : 0), ct);
    }

    // ==========================================================
    // helpers
    // ==========================================================

    private async Task<MarketplaceResult<MarketplaceInventoryDto>> ReadAsync(
        int staffId,
        int inventoryId,
        CancellationToken ct)
    {
        var row = await _db.MarketplaceInventory.AsNoTracking()
            .Include(i => i.Product)
            .Include(i => i.Variant)
            .FirstOrDefaultAsync(i => i.InventoryId == inventoryId, ct);

        return row is null
            ? MarketplaceResult<MarketplaceInventoryDto>.NotFound("Inventory row not found.")
            : MarketplaceResult<MarketplaceInventoryDto>.Ok(ToDto(row));
    }

    private static MarketplaceInventoryDto ToDto(MarketplaceInventory row) => new()
    {
        InventoryId = row.InventoryId,
        ProductId = row.ProductId,
        ProductName = row.Product?.Name ?? "",
        Category = row.Product?.Category ?? "",
        SubCategory = row.Product?.SubCategory ?? "",
        VariantId = row.VariantId,
        VariantName = row.Variant?.VariantValue ?? "",
        PriceCentavos = (row.Product?.PriceCentavos ?? 0) + (row.Variant?.PriceAdjustmentCentavos ?? 0),
        ImageUrl = row.Product?.ImageUrl ?? "",
        StockQuantity = row.StockQuantity,
        ReservedQuantity = row.ReservedQuantity,
        LowStockThreshold = row.LowStockThreshold,
        UpdatedAt = row.UpdatedAt
    };

    private const string StaffOnly = "Marketplace Staff access required.";
}