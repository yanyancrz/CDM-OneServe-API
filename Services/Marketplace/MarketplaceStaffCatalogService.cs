using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Models.Marketplace;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// The staff side of the catalog: products, their size variants, the approved
/// campus delivery locations, and the marketplace settings.
///
/// This is the ONLY place products and locations are written. Buyers have no
/// write path at all - they browse and order, staff supply.
///
/// Two rules are enforced here rather than trusted from the client:
///   * a product's category/subcategory must be one of the spec's fixed lists,
///     so the catalog can never drift into "Other";
///   * campus delivery locations are a controlled list, because that list IS the
///     mechanism that keeps delivery on campus.
/// </summary>
public class MarketplaceStaffCatalogService
{
    private readonly AppDbContext _db;
    private readonly MarketplaceAccessService _access;
    private readonly MarketplaceCatalogService _catalog;

    public MarketplaceStaffCatalogService(
        AppDbContext db,
        MarketplaceAccessService access,
        MarketplaceCatalogService catalog)
    {
        _db = db;
        _access = access;
        _catalog = catalog;
    }

    // ==========================================================
    // products
    // ==========================================================

    /// <summary>
    /// Every product, including deactivated ones, so staff can see and bring back
    /// something they retired. Buyers only ever see the active list.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceProductDto>>> ListProductsAsync(
        int staffId,
        string? category,
        string? search,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<List<MarketplaceProductDto>>.Forbidden(StaffOnly);

        var query = _db.MarketplaceProducts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(category))
        {
            var wanted = category.Trim();
            query = query.Where(p => p.Category == wanted);
        }

        var products = await query
            .OrderBy(p => p.Category).ThenBy(p => p.SubCategory).ThenBy(p => p.Name)
            .ToListAsync(ct);

        var needle = (search ?? "").Trim();
        if (needle.Length > 0)
        {
            products = products.Where(p =>
                p.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                p.Category.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                p.SubCategory.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return MarketplaceResult<List<MarketplaceProductDto>>.Ok(await ToDtosAsync(products, ct));
    }

    public async Task<MarketplaceResult<MarketplaceProductDto>> GetProductAsync(
        int staffId,
        int productId,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<MarketplaceProductDto>.Forbidden(StaffOnly);

        var product = await _db.MarketplaceProducts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProductId == productId, ct);

        if (product is null)
            return MarketplaceResult<MarketplaceProductDto>.NotFound("Product not found.");

        var dtos = await ToDtosAsync(new List<MarketplaceProduct> { product }, ct);
        return MarketplaceResult<MarketplaceProductDto>.Ok(dtos[0]);
    }

    /// <summary>
    /// Creates or updates a product, and replaces its variant set in the same
    /// payload so sizes and stock are managed together.
    ///
    /// A variant's StockQuantity in the payload SEEDS its inventory row on first
    /// save. Later edits go through the Inventory screen instead, because stock
    /// there is a running count - restocking must be additive, not a blind
    /// overwrite.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceProductDto>> SaveProductAsync(
        int staffId,
        SaveProductRequest request,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<MarketplaceProductDto>.Forbidden(StaffOnly);

        var name = (request.Name ?? "").Trim();
        if (name.Length < 2)
            return MarketplaceResult<MarketplaceProductDto>.BadRequest("Enter the product name.");

        var category = (request.Category ?? "").Trim();

        if (!MarketplaceCategories.IsValidCategory(category))
            return MarketplaceResult<MarketplaceProductDto>.BadRequest(
                $"Category must be one of: {string.Join(", ", MarketplaceCategories.All)}.");

        var subCategory = (request.SubCategory ?? "").Trim();

        // Uniforms must pick one of the eight named types; food and merchandise
        // have their own short lists. This is the spec's fixed taxonomy.
        if (!MarketplaceCategories.IsValidSubCategory(category, subCategory))
        {
            return MarketplaceResult<MarketplaceProductDto>.BadRequest(
                $"Choose a valid {category} type: " +
                $"{string.Join(", ", MarketplaceCategories.SubCategoriesFor(category))}.");
        }

        var variants = request.Variants ?? new List<SaveVariantRequest>();
        var hasVariants = request.HasVariants || variants.Count > 0;

        if (hasVariants)
        {
            var values = variants
                .Where(v => !string.IsNullOrWhiteSpace(v.VariantValue))
                .Select(v => v.VariantValue.Trim())
                .ToList();

            if (values.Count == 0)
                return MarketplaceResult<MarketplaceProductDto>.BadRequest(
                    "Add at least one size for this product.");

            if (values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
                return MarketplaceResult<MarketplaceProductDto>.BadRequest(
                    "Each size must be unique.");
        }

        var product = request.ProductId is int existingId
            ? await _db.MarketplaceProducts
                .FirstOrDefaultAsync(p => p.ProductId == existingId, ct)
            : null;

        if (request.ProductId is int wantedId && product is null)
            return MarketplaceResult<MarketplaceProductDto>.NotFound("Product not found.");

        var isNew = product is null;
        product ??= new MarketplaceProduct { CreatedAt = DateTime.UtcNow };

        product.Name = name;
        product.Description = (request.Description ?? "").Trim();
        product.Category = category;
        product.SubCategory = subCategory;
        product.PriceCentavos = Math.Max(0, request.PriceCentavos);
        // ImageUrl is deliberately NOT set here. A photo is an uploaded file applied
        // by the dedicated image endpoint; letting a product save overwrite it
        // would be a second, unguarded way to change it.
        product.HasVariants = hasVariants;
        product.IsActive = request.IsActive;
        product.UpdatedAt = DateTime.UtcNow;

        if (isNew) _db.MarketplaceProducts.Add(product);
        await _db.SaveChangesAsync(ct);

        var savedVariants = await SaveVariantsAsync(product.ProductId, variants, hasVariants, ct);

        var dto = await ToDtosAsync(new List<MarketplaceProduct> { product }, ct);
        return MarketplaceResult<MarketplaceProductDto>.Ok(
            dto[0],
            isNew ? "Product added." : "Product updated.");
    }

    /// <summary>
    /// Deactivates a product rather than deleting it, because past orders still
    /// point at it. Deleting would leave historical order lines dangling.
    /// </summary>
    public async Task<MarketplaceResult<object>> ArchiveProductAsync(
        int staffId,
        int productId,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<object>.Forbidden(StaffOnly);

        var product = await _db.MarketplaceProducts
            .FirstOrDefaultAsync(p => p.ProductId == productId, ct);

        if (product is null) return MarketplaceResult<object>.NotFound("Product not found.");

        product.IsActive = false;
        product.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<object>.Ok(new { }, "Product removed from the catalog.");
    }

    public async Task<MarketplaceResult<object>> RestoreProductAsync(
        int staffId,
        int productId,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<object>.Forbidden(StaffOnly);

        var product = await _db.MarketplaceProducts
            .FirstOrDefaultAsync(p => p.ProductId == productId, ct);

        if (product is null) return MarketplaceResult<object>.NotFound("Product not found.");

        product.IsActive = true;
        product.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<object>.Ok(new { }, "Product is back in the catalog.");
    }

    // ==========================================================
    // campus locations
    // ==========================================================

    /// <summary>Every location, including inactive, so staff can retire one safely.</summary>
    public async Task<MarketplaceResult<List<MarketplaceCampusLocationDto>>> ListLocationsAsync(
        int staffId,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<List<MarketplaceCampusLocationDto>>.Forbidden(StaffOnly);

        var rows = await _db.MarketplaceCampusLocations.AsNoTracking()
            .OrderBy(l => l.SortOrder).ThenBy(l => l.LocationName)
            .ToListAsync(ct);

        return MarketplaceResult<List<MarketplaceCampusLocationDto>>.Ok(rows
            .Select(ToDto).ToList());
    }

    /// <summary>
    /// Adds or edits an approved campus delivery destination.
    ///
    /// This list is the whole "delivery stays on campus" guarantee, so it is
    /// staff-managed rather than free text - and nothing outside it can ever be
    /// selected at checkout.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceCampusLocationDto>> SaveLocationAsync(
        int staffId,
        int? locationId,
        SaveCampusLocationRequest request,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<MarketplaceCampusLocationDto>.Forbidden(StaffOnly);

        var name = (request.LocationName ?? "").Trim();
        if (name.Length < 2)
            return MarketplaceResult<MarketplaceCampusLocationDto>.BadRequest(
                "Enter the location name.");

        var row = locationId is int id
            ? await _db.MarketplaceCampusLocations.FirstOrDefaultAsync(l => l.LocationId == id, ct)
            : null;

        if (locationId is int wantedId && row is null)
            return MarketplaceResult<MarketplaceCampusLocationDto>.NotFound("Location not found.");

        var isNew = row is null;
        row ??= new MarketplaceCampusLocation { CreatedAt = DateTime.UtcNow };

        row.LocationName = name;
        row.Description = (request.Description ?? "").Trim();
        row.AllowsDetails = request.AllowsDetails;
        row.SortOrder = Math.Max(0, request.SortOrder);
        row.IsActive = request.IsActive;
        row.UpdatedAt = DateTime.UtcNow;

        if (isNew) _db.MarketplaceCampusLocations.Add(row);
        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<MarketplaceCampusLocationDto>.Ok(
            ToDto(row), isNew ? "Campus location added." : "Campus location updated.");
    }

    public async Task<MarketplaceResult<object>> ArchiveLocationAsync(
        int staffId,
        int locationId,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<object>.Forbidden(StaffOnly);

        var row = await _db.MarketplaceCampusLocations
            .FirstOrDefaultAsync(l => l.LocationId == locationId, ct);

        if (row is null) return MarketplaceResult<object>.NotFound("Location not found.");

        row.IsActive = false;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<object>.Ok(new { },
            "Location removed. Existing orders keep their destination.");
    }

    // ==========================================================
    // settings
    // ==========================================================

    /// <summary>
    /// The current pickup location/instructions and campus delivery fee.
    /// Staff-only because it is the operations panel, not buyer copy.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceSettingsDto>> GetSettingsAsync(
        int staffId,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<MarketplaceSettingsDto>.Forbidden(StaffOnly);

        var settings = await _catalog.LoadSettingsAsync(ct);

        return MarketplaceResult<MarketplaceSettingsDto>.Ok(new MarketplaceSettingsDto
        {
            PickupLocation = settings.PickupLocation,
            PickupInstructions = settings.PickupInstructions,
            DeliveryEnabled = settings.DeliveryEnabled,
            DeliveryFeeCentavos = settings.DeliveryFeeCentavos,
            UpdatedAt = settings.UpdatedAt
        });
    }

    /// <summary>
    /// Saves the pickup details and the campus delivery fee.
    ///
    /// The fee is staff-configured and may be 0 - free campus delivery is a valid
    /// setting. It is clamped rather than rejected so a typo like 5000000 is
    /// caught without needing a special error path.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceSettingsDto>> SaveSettingsAsync(
        int staffId,
        SaveSettingsRequest request,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<MarketplaceSettingsDto>.Forbidden(StaffOnly);

        var settings = await _catalog.LoadSettingsAsync(ct);

        settings.PickupLocation = (request.PickupLocation ?? "").Trim();
        settings.PickupInstructions = (request.PickupInstructions ?? "").Trim();
        settings.DeliveryEnabled = request.DeliveryEnabled;

        // 0 is meaningful here - it means free campus delivery, not "unset".
        settings.DeliveryFeeCentavos = Math.Clamp(request.DeliveryFeeCentavos, 0, 1000000);
        settings.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<MarketplaceSettingsDto>.Ok(new MarketplaceSettingsDto
        {
            PickupLocation = settings.PickupLocation,
            PickupInstructions = settings.PickupInstructions,
            DeliveryEnabled = settings.DeliveryEnabled,
            DeliveryFeeCentavos = settings.DeliveryFeeCentavos,
            UpdatedAt = settings.UpdatedAt
        }, "Settings saved.");
    }

    // ==========================================================
    // internal helpers
    // ==========================================================

    /// <summary>
    /// Replaces a product's variant set. Variants already in the payload are
    /// updated in place (keeping their inventory); variants missing from it are
    /// deactivated rather than deleted, because their inventory rows and past
    /// order lines still reference them.
    /// </summary>
    private async Task<List<MarketplaceProductVariant>> SaveVariantsAsync(
        int productId,
        List<SaveVariantRequest> payload,
        bool hasVariants,
        CancellationToken ct)
    {
        var existing = await _db.MarketplaceProductVariants
            .Where(v => v.ProductId == productId)
            .ToListAsync(ct);

        if (!hasVariants)
        {
            foreach (var variant in existing.Where(v => v.IsActive))
            {
                variant.IsActive = false;
                await ReleaseInventoryAsync(productId, variant.VariantId, ct);
            }
            return existing;
        }

        var kept = new List<MarketplaceProductVariant>();

        for (var i = 0; i < payload.Count; i++)
        {
            var item = payload[i];
            var value = (item.VariantValue ?? "").Trim();
            if (value.Length == 0) continue;

            var variant = item.VariantId is int variantId
                ? existing.FirstOrDefault(v => v.VariantId == variantId && v.ProductId == productId)
                : null;

            var isNew = variant is null;
            variant ??= new MarketplaceProductVariant { ProductId = productId };

            variant.VariantName = string.IsNullOrWhiteSpace(item.VariantName) ? "Size" : item.VariantName.Trim();
            variant.VariantValue = value;
            variant.PriceAdjustmentCentavos = item.PriceAdjustmentCentavos;
            variant.SortOrder = item.SortOrder > 0 ? item.SortOrder : i + 1;
            variant.IsActive = item.IsActive;

            if (isNew) _db.MarketplaceProductVariants.Add(variant);

            await _db.SaveChangesAsync(ct);

            // First save seeds the stock; later stock changes go through the
            // Inventory screen so a restock is additive.
            if (isNew)
            {
                await _db.MarketplaceInventory.AddAsync(new MarketplaceInventory
                {
                    ProductId = productId,
                    VariantId = variant.VariantId,
                    StockQuantity = Math.Max(0, item.StockQuantity),
                    ReservedQuantity = 0,
                    LowStockThreshold = 5,
                    UpdatedAt = DateTime.UtcNow
                }, ct);
                await _db.SaveChangesAsync(ct);
            }

            kept.Add(variant);
        }

        // Anything the payload dropped is deactivated.
        foreach (var variant in existing.Where(v => v.IsActive && !kept.Any(k => k.VariantId == v.VariantId)))
        {
            variant.IsActive = false;
            await ReleaseInventoryAsync(productId, variant.VariantId, ct);
        }

        if (kept.Count > 0) await _db.SaveChangesAsync(ct);

        return kept;
    }

    /// <summary>
    /// Drops the reservation hold on a variant that is leaving the catalog and
    /// zeroes its stock, so an inactive size can never look orderable.
    /// </summary>
    private async Task ReleaseInventoryAsync(int productId, int variantId, CancellationToken ct)
    {
        await _db.MarketplaceInventory
            .Where(i => i.ProductId == productId && i.VariantId == variantId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.StockQuantity, 0)
                .SetProperty(i => i.ReservedQuantity, 0)
                .SetProperty(i => i.UpdatedAt, DateTime.UtcNow), ct);
    }

    /// <summary>
    /// Makes sure a one-size product has exactly one inventory row (VariantId =
    /// null). Without this, a product created with no variants would show zero
    /// stock forever.
    /// </summary>
    private async Task EnsureOneSizeInventoryAsync(MarketplaceProduct product, CancellationToken ct)
    {
        if (product.HasVariants) return;

        var row = await _db.MarketplaceInventory
            .FirstOrDefaultAsync(i => i.ProductId == product.ProductId && i.VariantId == null, ct);

        if (row is null)
        {
            _db.MarketplaceInventory.Add(new MarketplaceInventory
            {
                ProductId = product.ProductId,
                VariantId = null,
                StockQuantity = 0,
                ReservedQuantity = 0,
                LowStockThreshold = 5,
                UpdatedAt = DateTime.UtcNow
            });
        }
    }

    private async Task<List<MarketplaceProductDto>> ToDtosAsync(
        IReadOnlyList<MarketplaceProduct> products,
        CancellationToken ct)
    {
        if (products.Count == 0) return new List<MarketplaceProductDto>();

        foreach (var product in products)
            await EnsureOneSizeInventoryAsync(product, ct);

        await _db.SaveChangesAsync(ct);

        var ids = products.Select(p => p.ProductId).ToList();

        var variants = await _db.MarketplaceProductVariants.AsNoTracking()
            .Where(v => ids.Contains(v.ProductId) && v.IsActive)
            .OrderBy(v => v.SortOrder).ThenBy(v => v.VariantValue)
            .ToListAsync(ct);

        var inventory = await _db.MarketplaceInventory.AsNoTracking()
            .Where(i => ids.Contains(i.ProductId))
            .ToListAsync(ct);

        var stockByKey = inventory.ToDictionary(i => (i.ProductId, i.VariantId));

        return products.Select(product =>
        {
            var rows = inventory.Where(i => i.ProductId == product.ProductId).ToList();

            var variantDtos = variants.Where(v => v.ProductId == product.ProductId)
                .Select(variant =>
                {
                    var available = stockByKey.TryGetValue((product.ProductId, variant.VariantId), out var row)
                        ? Math.Max(0, row.StockQuantity - row.ReservedQuantity)
                        : 0;

                    return new MarketplaceVariantDto
                    {
                        VariantId = variant.VariantId,
                        ProductId = variant.ProductId,
                        VariantName = variant.VariantName,
                        VariantValue = variant.VariantValue,
                        PriceAdjustmentCentavos = variant.PriceAdjustmentCentavos,
                        EffectivePriceCentavos = product.PriceCentavos + variant.PriceAdjustmentCentavos,
                        StockQuantity = stockByKey.GetValueOrDefault((product.ProductId, variant.VariantId))?.StockQuantity ?? 0,
                        ReservedQuantity = stockByKey.GetValueOrDefault((product.ProductId, variant.VariantId))?.ReservedQuantity ?? 0,
                        AvailableQuantity = available,
                        IsActive = variant.IsActive
                    };
                }).ToList();

            var stock = rows.Sum(i => i.StockQuantity);
            var reserved = rows.Sum(i => i.ReservedQuantity);

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
                StockQuantity = stock,
                ReservedQuantity = reserved,
                AvailableQuantity = Math.Max(0, stock - reserved),
                Variants = variantDtos
            };
        }).ToList();
    }

    private static MarketplaceCampusLocationDto ToDto(MarketplaceCampusLocation row) => new()
    {
        LocationId = row.LocationId,
        LocationName = row.LocationName,
        Description = row.Description,
        AllowsDetails = row.AllowsDetails,
        SortOrder = row.SortOrder
    };

    private const string StaffOnly = "Marketplace Staff access required.";
}