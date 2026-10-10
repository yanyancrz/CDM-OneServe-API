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
public class MarketplaceInventoryService
{
    private readonly AppDbContext _db;
    private readonly MarketplaceAccessService _access;

    public MarketplaceInventoryService(AppDbContext db, MarketplaceAccessService access)
    {
        _db = db;
        _access = access;
    }

    // ==========================================================
    // staff reads
    // ==========================================================

    /// <summary>Every inventory row, joined to its product and variant.</summary>
    public async Task<MarketplaceResult<List<MarketplaceInventoryDto>>> ListAsync(
        int staffId,
        string? search,
        string? stockState,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<List<MarketplaceInventoryDto>>.Forbidden(StaffOnly);

        var rows = await _db.MarketplaceInventory.AsNoTracking()
            .Include(i => i.Product)
            .Include(i => i.Variant)
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
        CancellationToken ct = default)
    {
        var all = await ListAsync(staffId, null, null, ct);
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
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceInventoryDto>> SaveAsync(
        int staffId,
        int inventoryId,
        SaveInventoryRequest request,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<MarketplaceInventoryDto>.Forbidden(StaffOnly);

        var row = await _db.MarketplaceInventory
            .FirstOrDefaultAsync(i => i.InventoryId == inventoryId, ct);

        if (row is null)
            return MarketplaceResult<MarketplaceInventoryDto>.NotFound("Inventory row not found.");

        if (request.StockQuantity < 0)
            return MarketplaceResult<MarketplaceInventoryDto>.BadRequest("Stock cannot be negative.");

        // Stock must stay at or above what is already reserved, otherwise the
        // shelf would promise less than the pending orders have claimed.
        if (request.StockQuantity < row.ReservedQuantity)
            return MarketplaceResult<MarketplaceInventoryDto>.BadRequest(
                $"{row.ReservedQuantity} unit(s) are reserved by open orders. " +
                "Cancel or complete those orders first.");

        row.StockQuantity = request.StockQuantity;
        row.LowStockThreshold = Math.Max(0, request.LowStockThreshold);
        row.UpdatedAt = DateTime.UtcNow;

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
        int quantity,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<MarketplaceInventoryDto>.Forbidden(StaffOnly);

        if (quantity <= 0)
            return MarketplaceResult<MarketplaceInventoryDto>.BadRequest("Enter a quantity to add.");

        var row = await _db.MarketplaceInventory
            .FirstOrDefaultAsync(i => i.InventoryId == inventoryId, ct);

        if (row is null)
            return MarketplaceResult<MarketplaceInventoryDto>.NotFound("Inventory row not found.");

        row.StockQuantity += quantity;
        row.UpdatedAt = DateTime.UtcNow;

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

        return affected == 1;
    }

    /// <summary>
    /// Releases a reservation - used when an order is cancelled or expires
    /// unpaid. The stock stays on the shelf; only the hold goes away.
    /// </summary>
    public async Task ReleaseAsync(
        int productId,
        int? variantId,
        int quantity,
        CancellationToken ct = default)
    {
        if (quantity <= 0) return;

        await _db.MarketplaceInventory
            .Where(i => i.ProductId == productId &&
                        i.VariantId == variantId &&
                        i.ReservedQuantity >= quantity)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.ReservedQuantity, i => i.ReservedQuantity - quantity)
                .SetProperty(i => i.UpdatedAt, DateTime.UtcNow), ct);
    }

    /// <summary>
    /// Consumes a reservation on completion: the unit leaves the shelf for good
    /// (Stock -1) and the hold is dropped (Reserved -1).
    /// </summary>
    public async Task ConsumeAsync(
        int productId,
        int? variantId,
        int quantity,
        CancellationToken ct = default)
    {
        if (quantity <= 0) return;

        await _db.MarketplaceInventory
            .Where(i => i.ProductId == productId &&
                        i.VariantId == variantId &&
                        i.StockQuantity >= quantity)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.StockQuantity, i => i.StockQuantity - quantity)
                .SetProperty(i => i.UpdatedAt, DateTime.UtcNow), ct);

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