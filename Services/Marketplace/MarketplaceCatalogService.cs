using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Models.Marketplace;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Everything a buyer browses: products, campus locations, checkout info and
/// the cart.
///
/// Reads only. Products and stock are changed by the staff services, never by a
/// buyer - that separation is enforced here by simply not offering a write path.
/// </summary>
public class MarketplaceCatalogService
{
    private readonly AppDbContext _db;
    private readonly MarketplaceAccessService _access;

    public MarketplaceCatalogService(AppDbContext db, MarketplaceAccessService access)
    {
        _db = db;
        _access = access;
    }

    // ==========================================================
    // who am I
    // ==========================================================

    /// <summary>
    /// The signed-in buyer, built entirely from the EXISTING OneServe account.
    /// This is the only place the marketplace learns who the user is, and it
    /// never writes any of it back.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceBuyerDto>> GetBuyerAsync(
        int userId,
        CancellationToken ct = default)
    {
        var buyer = await _access.RequireBuyerAsync(userId, ct);
        if (buyer.Actor is null) return MarketplaceAccessService.BuyerRefusal<MarketplaceBuyerDto>(buyer);

        return MarketplaceResult<MarketplaceBuyerDto>.Ok(new MarketplaceBuyerDto
        {
            UserId = buyer.Actor.UserId,
            FullName = buyer.Actor.User.FullName,
            IdNumber = buyer.Actor.User.IdNumber,
            Email = buyer.Actor.User.Email,
            AccountType = buyer.Actor.User.Role,
            Course = buyer.Actor.User.Course ?? "",
            YearLevel = buyer.Actor.User.YearLevel ?? "",
            Institute = buyer.Actor.User.Institute ?? "",
            ContactNumber = buyer.Actor.User.ContactNumber ?? ""
        });
    }

    // ==========================================================
    // products
    // ==========================================================

    /// <summary>
    /// The catalog, optionally narrowed to a category, a subcategory or a
    /// search term. Out-of-stock products are still returned - the UI shows them
    /// with the Add to Cart button disabled, which is friendlier than hiding
    /// them.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceProductDto>>> ListProductsAsync(
        string? category,
        string? subCategory,
        string? search,
        CancellationToken ct = default)
    {
        var query = _db.MarketplaceProducts.AsNoTracking().Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(category))
        {
            var wanted = category.Trim();
            query = query.Where(p => p.Category == wanted);
        }

        if (!string.IsNullOrWhiteSpace(subCategory))
        {
            var wanted = subCategory.Trim();
            query = query.Where(p => p.SubCategory == wanted);
        }

        var products = await query
            .OrderBy(p => p.Category).ThenBy(p => p.SubCategory).ThenBy(p => p.Name)
            .ToListAsync(ct);

        var needle = (search ?? "").Trim();
        if (needle.Length > 0)
        {
            // Matches name, category and subcategory, so searching "uniform"
            // finds Wash Day Uniform, PE T-Shirt and so on.
            products = products
                .Where(p =>
                    p.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                    p.Category.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                    p.SubCategory.Contains(needle, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return MarketplaceResult<List<MarketplaceProductDto>>.Ok(
            await ToProductDtosAsync(products, ct));
    }

    /// <summary>One product with its variants and live stock.</summary>
    public async Task<MarketplaceResult<MarketplaceProductDto>> GetProductAsync(
        int productId,
        CancellationToken ct = default)
    {
        var product = await _db.MarketplaceProducts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProductId == productId && p.IsActive, ct);

        if (product is null)
            return MarketplaceResult<MarketplaceProductDto>.NotFound("This product is unavailable.");

        var dto = await ToProductDtosAsync(new List<MarketplaceProduct> { product }, ct);

        return MarketplaceResult<MarketplaceProductDto>.Ok(dto[0]);
    }

    // ==========================================================
    // campus locations
    // ==========================================================

    /// <summary>
    /// The approved campus delivery destinations. This list is what makes
    /// "delivery stays on campus" enforceable - a buyer picks from here instead
    /// of typing an address.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceCampusLocationDto>>> ListCampusLocationsAsync(
        CancellationToken ct = default)
    {
        var rows = await _db.MarketplaceCampusLocations.AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.LocationName)
            .Select(l => new MarketplaceCampusLocationDto
            {
                LocationId = l.LocationId,
                LocationName = l.LocationName,
                Description = l.Description,
                AllowsDetails = l.AllowsDetails,
                SortOrder = l.SortOrder
            })
            .ToListAsync(ct);

        return MarketplaceResult<List<MarketplaceCampusLocationDto>>.Ok(rows);
    }

    // ==========================================================
    // checkout info
    // ==========================================================

    /// <summary>
    /// What checkout needs before the buyer picks a fulfillment method: the real
    /// pickup location and instructions, and the staff-configured delivery fee.
    /// Neither value is hardcoded here - both come from marketplace_settings.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceCheckoutInfoDto>> GetCheckoutInfoAsync(
        CancellationToken ct = default)
    {
        var settings = await LoadSettingsAsync(ct);

        return MarketplaceResult<MarketplaceCheckoutInfoDto>.Ok(new MarketplaceCheckoutInfoDto
        {
            PickupLocation = settings.PickupLocation,
            PickupInstructions = settings.PickupInstructions,
            DeliveryEnabled = settings.DeliveryEnabled,
            DeliveryFeeCentavos = settings.DeliveryEnabled ? settings.DeliveryFeeCentavos : 0,
            DeliveryRestrictionNotice =
                "Campus Delivery is strictly within the CDM campus. Choose an approved " +
                "campus location below - home addresses and addresses outside campus " +
                "cannot be used."
        });
    }

    /// <summary>The single settings row, created on first read.</summary>
    internal async Task<MarketplaceSettings> LoadSettingsAsync(CancellationToken ct)
    {
        var settings = await _db.MarketplaceSettings.FirstOrDefaultAsync(s => s.Id == 1, ct);

        if (settings is not null) return settings;

        settings = new MarketplaceSettings { Id = 1 };
        _db.MarketplaceSettings.Add(settings);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request created it first - re-read the winner's row.
            _db.Entry(settings).State = EntityState.Detached;
            settings = await _db.MarketplaceSettings.FirstOrDefaultAsync(s => s.Id == 1, ct);
        }

        return settings ?? new MarketplaceSettings { Id = 1 };
    }

    // ==========================================================
    // cart
    // ==========================================================

    /// <summary>
    /// The buyer's cart, each line carrying the live stock so an unavailable
    /// line is visible but obviously not orderable.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceCartItemDto>>> GetCartAsync(
        int userId,
        CancellationToken ct = default)
    {
        var buyer = await _access.RequireBuyerAsync(userId, ct);
        if (buyer.Actor is null) return MarketplaceAccessService.BuyerRefusal<List<MarketplaceCartItemDto>>(buyer);

        var items = await _db.MarketplaceCartItems.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.CartItemId)
            .ToListAsync(ct);

        var ids = items.Select(i => i.ProductId).Distinct().ToList();
        var variants = items.Where(i => i.VariantId.HasValue).Select(i => i.VariantId!.Value).ToList();

        var products = await _db.MarketplaceProducts.AsNoTracking()
            .Where(p => ids.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId, ct);

        var variantRows = await _db.MarketplaceProductVariants.AsNoTracking()
            .Where(v => variants.Contains(v.VariantId))
            .ToDictionaryAsync(v => v.VariantId, ct);

        var inventory = await _db.MarketplaceInventory.AsNoTracking()
            .Where(i => ids.Contains(i.ProductId))
            .ToListAsync(ct);

        // Available per (product, variant). One-size products use VariantId = null.
        var stockByKey = inventory.ToDictionary(
            i => (i.ProductId, i.VariantId),
            i => i.StockQuantity - i.ReservedQuantity);

        var dtos = new List<MarketplaceCartItemDto>();

        foreach (var item in items)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                // The product was removed - drop the stale line.
                continue;
            }

            var unit = product.PriceCentavos;
            string variantName = "";

            if (item.VariantId is int variantId && variantRows.TryGetValue(variantId, out var variant))
            {
                unit += variant.PriceAdjustmentCentavos;
                variantName = variant.VariantValue;
            }

            var available = stockByKey.TryGetValue((item.ProductId, item.VariantId), out var availableStock)
                ? availableStock
                : 0;

            dtos.Add(new MarketplaceCartItemDto
            {
                CartItemId = item.CartItemId,
                ProductId = product.ProductId,
                ProductName = product.Name,
                Category = product.Category,
                SubCategory = product.SubCategory,
                ImageUrl = product.ImageUrl ?? "",
                VariantId = item.VariantId,
                VariantName = variantName,
                UnitPriceCentavos = unit,
                Quantity = item.Quantity,
                SubtotalCentavos = unit * item.Quantity,
                AvailableQuantity = available
            });
        }

        return MarketplaceResult<List<MarketplaceCartItemDto>>.Ok(dtos);
    }

    /// <summary>Adds a line, or raises the quantity if that exact line exists.</summary>
    public async Task<MarketplaceResult<object>> AddToCartAsync(
        int userId,
        int productId,
        int? variantId,
        int quantity,
        CancellationToken ct = default)
    {
        var buyer = await _access.RequireBuyerAsync(userId, ct);
        if (buyer.Actor is null) return MarketplaceAccessService.BuyerRefusal<object>(buyer);

        if (quantity < 1 || quantity > 100)
            return MarketplaceResult<object>.BadRequest("Choose a quantity between 1 and 100.");

        var product = await _db.MarketplaceProducts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProductId == productId && p.IsActive, ct);

        if (product is null)
            return MarketplaceResult<object>.NotFound("This product is unavailable.");

        // A product with variants must have a valid, active one selected, or a
        // one-size product must not have one.
        if (product.HasVariants)
        {
            if (variantId is not int wanted)
                return MarketplaceResult<object>.BadRequest("Please choose a size first.");

            var variantExists = await _db.MarketplaceProductVariants.AsNoTracking()
                .AnyAsync(v => v.VariantId == wanted && v.ProductId == productId && v.IsActive, ct);

            if (!variantExists)
                return MarketplaceResult<object>.BadRequest("That size is not available.");
        }
        else
        {
            variantId = null;
        }

        var available = await GetAvailableAsync(productId, variantId, ct);
        if (available <= 0)
            return MarketplaceResult<object>.BadRequest("This item is out of stock.");

        var existing = await _db.MarketplaceCartItems
            .FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == productId && c.VariantId == variantId, ct);

        var target = (existing?.Quantity ?? 0) + quantity;

        // Never let the cart promise more than the shelf holds.
        if (target > available)
            return MarketplaceResult<object>.BadRequest(
                $"Only {available} left in stock.");

        if (existing is null)
        {
            _db.MarketplaceCartItems.Add(new MarketplaceCartItem
            {
                UserId = userId,
                ProductId = productId,
                VariantId = variantId,
                Quantity = quantity
            });
        }
        else
        {
            existing.Quantity = target;
        }

        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<object>.Ok(new { }, "Added to your cart.");
    }

    /// <summary>Sets an exact quantity. 0 or less removes the line.</summary>
    public async Task<MarketplaceResult<object>> SetCartQuantityAsync(
        int userId,
        int cartItemId,
        int quantity,
        CancellationToken ct = default)
    {
        var buyer = await _access.RequireBuyerAsync(userId, ct);
        if (buyer.Actor is null) return MarketplaceAccessService.BuyerRefusal<object>(buyer);

        var item = await _db.MarketplaceCartItems
            .FirstOrDefaultAsync(c => c.CartItemId == cartItemId, ct);

        // Ownership check: a cartItemId from somebody else's cart is not found.
        if (item is null || item.UserId != userId)
            return MarketplaceResult<object>.NotFound("That cart item no longer exists.");

        if (quantity <= 0)
        {
            _db.MarketplaceCartItems.Remove(item);
            await _db.SaveChangesAsync(ct);
            return MarketplaceResult<object>.Ok(new { }, "Removed from your cart.");
        }

        var available = await GetAvailableAsync(item.ProductId, item.VariantId, ct);
        if (quantity > available)
            return MarketplaceResult<object>.BadRequest($"Only {available} left in stock.");

        item.Quantity = quantity;
        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<object>.Ok(new { });
    }

    public async Task<MarketplaceResult<object>> ClearCartAsync(
        int userId,
        CancellationToken ct = default)
    {
        var buyer = await _access.RequireBuyerAsync(userId, ct);
        if (buyer.Actor is null) return MarketplaceAccessService.BuyerRefusal<object>(buyer);

        var lines = await _db.MarketplaceCartItems
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);

        _db.MarketplaceCartItems.RemoveRange(lines);
        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<object>.Ok(new { }, "Cart cleared.");
    }

    // ==========================================================
    // helpers
    // ==========================================================

    /// <summary>Stock minus reservations for one exact product+variant.</summary>
    internal async Task<int> GetAvailableAsync(int productId, int? variantId, CancellationToken ct)
    {
        var inventory = await _db.MarketplaceInventory.AsNoTracking()
            .Where(i => i.ProductId == productId && i.VariantId == variantId)
            .Select(i => new { i.StockQuantity, i.ReservedQuantity })
            .FirstOrDefaultAsync(ct);

        if (inventory is null) return 0;

        return Math.Max(0, inventory.StockQuantity - inventory.ReservedQuantity);
    }

    /// <summary>
    /// Products plus their variants and live stock. Reads inventory once rather
    /// than per product, so a 40-item catalog is still a single round trip.
    /// </summary>
    private async Task<List<MarketplaceProductDto>> ToProductDtosAsync(
        IReadOnlyList<MarketplaceProduct> products,
        CancellationToken ct)
    {
        if (products.Count == 0) return new List<MarketplaceProductDto>();

        var ids = products.Select(p => p.ProductId).ToList();

        var variants = await _db.MarketplaceProductVariants.AsNoTracking()
            .Where(v => ids.Contains(v.ProductId) && v.IsActive)
            .OrderBy(v => v.SortOrder).ThenBy(v => v.VariantValue)
            .ToListAsync(ct);

        var inventory = await _db.MarketplaceInventory.AsNoTracking()
            .Where(i => ids.Contains(i.ProductId))
            .ToListAsync(ct);

        var inventoryByKey = inventory.ToDictionary(i => (i.ProductId, i.VariantId));
        var variantsByProduct = variants.GroupBy(v => v.ProductId).ToDictionary(g => g.Key, g => g.ToList());

        return products.Select(product =>
        {
            var rows = inventory.Where(i => i.ProductId == product.ProductId).ToList();

            var variantDtos = variantsByProduct.TryGetValue(product.ProductId, out var mine)
                ? mine.Select(variant =>
                {
                    var stock = inventoryByKey.TryGetValue((variant.ProductId, variant.VariantId), out var row)
                        ? row
                        : null;

                    var available = stock is null ? 0 : Math.Max(0, stock.StockQuantity - stock.ReservedQuantity);

                    return new MarketplaceVariantDto
                    {
                        VariantId = variant.VariantId,
                        ProductId = variant.ProductId,
                        VariantName = variant.VariantName,
                        VariantValue = variant.VariantValue,
                        PriceAdjustmentCentavos = variant.PriceAdjustmentCentavos,
                        EffectivePriceCentavos = product.PriceCentavos + variant.PriceAdjustmentCentavos,
                        StockQuantity = stock?.StockQuantity ?? 0,
                        ReservedQuantity = stock?.ReservedQuantity ?? 0,
                        AvailableQuantity = available,
                        IsActive = variant.IsActive
                    };
                }).ToList()
                : new List<MarketplaceVariantDto>();

            var stockTotal = rows.Sum(i => i.StockQuantity);
            var reservedTotal = rows.Sum(i => i.ReservedQuantity);

            return new MarketplaceProductDto
            {
                ProductId = product.ProductId,
                Name = product.Name,
                Description = product.Description,
                Category = product.Category,
                SubCategory = product.SubCategory,
                PriceCentavos = product.PriceCentavos,
                ImageUrl = product.ImageUrl ?? "",
                HasVariants = product.HasVariants,
                IsActive = product.IsActive,
                CreatedAt = product.CreatedAt,
                UpdatedAt = product.UpdatedAt,
                StockQuantity = stockTotal,
                ReservedQuantity = reservedTotal,
                AvailableQuantity = Math.Max(0, stockTotal - reservedTotal),
                Variants = variantDtos
            };
        }).ToList();
    }
}