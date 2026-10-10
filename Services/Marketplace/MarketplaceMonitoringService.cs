using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Marketplace;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Marketplace monitoring for the existing CDM OneServe Admin.
///
/// The Admin MONITORS. Every method here is a READ: there is deliberately no
/// admin endpoint that can edit a Student/Faculty account, delete one, touch
/// inventory, process an order, manage a product, or answer a chat as
/// Marketplace Staff. Account management stays where it already lives - in the
/// OneServe admin.
///
/// The marketplace only READS the shared user table, and never writes to it.
/// </summary>
public class MarketplaceMonitoringService
{
    private readonly AppDbContext _db;
    private readonly MarketplaceAccessService _access;

    public MarketplaceMonitoringService(AppDbContext db, MarketplaceAccessService access)
    {
        _db = db;
        _access = access;
    }

    // ==========================================================
    // overview
    // ==========================================================

    /// <summary>The Admin's headline Marketplace numbers.</summary>
    public async Task<MarketplaceResult<MarketplaceMonitoringOverviewDto>> GetOverviewAsync(
        int adminId,
        CancellationToken ct = default)
    {
        if (!await _access.IsAdminAsync(adminId, ct))
            return MarketplaceResult<MarketplaceMonitoringOverviewDto>.Forbidden(AdminOnly);

        var now = DateTime.UtcNow;
        var today = now.Date;
        var week = today.AddDays(-7);
        var month = today.AddDays(-30);

        var orders = await _db.MarketplaceOrders.AsNoTracking().ToListAsync(ct);

        // Sales count completed orders only - a cancelled or unpaid order is not revenue.
        var completed = orders
            .Where(o => o.Status == MarketplaceOrderStatus.Completed)
            .ToList();

        var buyers = await _db.Users.AsNoTracking()
            .Where(u => u.Role == "Student" || u.Role == "Faculty")
            .Select(u => u.Role)
            .ToListAsync(ct);

        return MarketplaceResult<MarketplaceMonitoringOverviewDto>.Ok(new MarketplaceMonitoringOverviewDto
        {
            TotalSalesCentavos = completed.Sum(o => o.TotalCentavos),
            TodaySalesCentavos = completed.Where(o => o.CompletedAt >= today).Sum(o => o.TotalCentavos),
            WeekSalesCentavos = completed.Where(o => o.CompletedAt >= week).Sum(o => o.TotalCentavos),
            MonthSalesCentavos = completed.Where(o => o.CompletedAt >= month).Sum(o => o.TotalCentavos),
            TotalOrders = orders.Count,
            CompletedOrders = completed.Count,
            CancelledOrders = orders.Count(o => o.Status == MarketplaceOrderStatus.Cancelled),
            PendingOrders = orders.Count(o => o.Status == MarketplaceOrderStatus.Pending),
            PickupOrders = orders.Count(o => o.FulfillmentMethod == MarketplaceFulfillment.Pickup),
            CampusDeliveryOrders = orders.Count(o => o.FulfillmentMethod == MarketplaceFulfillment.CampusDelivery),
            StudentBuyers = buyers.Count(r => r == "Student"),
            FacultyBuyers = buyers.Count(r => r == "Faculty")
        });
    }

    // ==========================================================
    // accounts (read-only)
    // ==========================================================

    /// <summary>
    /// Marketplace information about Student and Faculty accounts.
    ///
    /// READ-ONLY. The caller cannot edit or delete any of this through the
    /// marketplace - there is no endpoint for it, by design.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceAccountMonitoringDto>>> ListAccountsAsync(
        int adminId,
        string? accountType,
        string? search,
        CancellationToken ct = default)
    {
        if (!await _access.IsAdminAsync(adminId, ct))
            return MarketplaceResult<List<MarketplaceAccountMonitoringDto>>.Forbidden(AdminOnly);

        var users = await _db.Users.AsNoTracking()
            .Where(u => u.Role == "Student" || u.Role == "Faculty")
            .ToListAsync(ct);

        var ids = users.Select(u => u.Id).ToList();

        // Only accounts that have actually ordered show up, so the list is
        // "marketplace accounts" rather than a copy of the whole student body.
        var orders = await _db.MarketplaceOrders.AsNoTracking()
            .Where(o => ids.Contains(o.UserId))
            .ToListAsync(ct);

        var byUser = orders
            .GroupBy(o => o.UserId)
            .ToDictionary(g => g.Key, g => new
            {
                Count = g.Count(),
                Completed = g.Count(o => o.Status == MarketplaceOrderStatus.Completed),
                Spent = g
                    .Where(o => o.Status == MarketplaceOrderStatus.Completed)
                    .Sum(o => o.TotalCentavos),
                Last = g.Max(o => o.CreatedAt)
            });

        var rows = users
            .Where(u => byUser.ContainsKey(u.Id))
            .Select(u =>
            {
                var stats = byUser[u.Id];

                return new MarketplaceAccountMonitoringDto
                {
                    UserId = u.Id,
                    FullName = u.FullName,
                    IdNumber = u.IdNumber,
                    Email = u.Email,
                    AccountType = u.Role,
                    Course = u.Course ?? "",
                    YearLevel = u.YearLevel ?? "",
                    Institute = u.Institute ?? "",
                    OrderCount = stats.Count,
                    CompletedOrderCount = stats.Completed,
                    TotalPurchaseCentavos = stats.Spent,
                    LastOrderAt = stats.Last
                };
            })
            .OrderByDescending(a => a.LastOrderAt)
            .ToList();

        if (!string.IsNullOrWhiteSpace(accountType))
        {
            var wanted = accountType.Trim();
            rows = rows
                .Where(r => string.Equals(r.AccountType, wanted, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var needle = (search ?? "").Trim();
        if (needle.Length > 0)
        {
            rows = rows.Where(r =>
                r.FullName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                r.IdNumber.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                r.Email.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return MarketplaceResult<List<MarketplaceAccountMonitoringDto>>.Ok(rows);
    }

    // ==========================================================
    // transactions (read-only)
    // ==========================================================

    /// <summary>
    /// Every Marketplace transaction, for the Admin's monitoring table. Same
    /// data the staff sees, but reachable without operating anything.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceTransactionDto>>> ListTransactionsAsync(
        int adminId,
        string? status,
        string? method,
        string? accountType,
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default)
    {
        if (!await _access.IsAdminAsync(adminId, ct))
            return MarketplaceResult<List<MarketplaceTransactionDto>>.Forbidden(AdminOnly);

        var orders = await _db.MarketplaceOrders.AsNoTracking()
            .Include(o => o.CampusLocation)
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .Take(TransactionLimit)
            .ToListAsync(ct);

        var users = await _db.Users.AsNoTracking()
            .Where(u => orders.Select(o => o.UserId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        var rows = orders.Select(o =>
        {
            var buyer = users.GetValueOrDefault(o.UserId);

            return new MarketplaceTransactionDto
            {
                OrderId = o.OrderId,
                OrderReference = o.OrderReference,
                BuyerName = buyer?.FullName ?? "",
                AccountType = buyer?.Role ?? "",
                CreatedAt = o.CreatedAt,
                FulfillmentMethod = o.FulfillmentMethod,
                FulfillmentLabel = MarketplaceFulfillment.Label(o.FulfillmentMethod),
                ItemCount = o.Items.Sum(i => i.Quantity),
                TotalCentavos = o.TotalCentavos,
                Status = o.Status,
                CampusLocationName = o.CampusLocation?.LocationName,
                SpecificLocation = o.SpecificLocation,
                RecipientName = o.RecipientName,
                ContactNumber = o.ContactNumber,
                DeliveryInstructions = o.DeliveryInstructions,
                Items = o.Items.Select(i => new MarketplaceOrderItemDto
                {
                    OrderItemId = i.OrderItemId,
                    ProductId = i.ProductId,
                    ProductName = i.ProductName,
                    VariantId = i.VariantId,
                    VariantName = i.VariantName,
                    Quantity = i.Quantity,
                    UnitPriceCentavos = i.UnitPriceCentavos,
                    SubtotalCentavos = i.SubtotalCentavos
                }).ToList()
            };
        }).ToList();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = status.Trim();
            rows = rows.Where(r => string.Equals(r.Status, wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(method))
        {
            var wanted = MarketplaceFulfillment.Normalize(method);
            rows = rows.Where(r => r.FulfillmentMethod == wanted).ToList();
        }

        if (!string.IsNullOrWhiteSpace(accountType))
        {
            var wanted = accountType.Trim();
            rows = rows
                .Where(r => string.Equals(r.AccountType, wanted, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (from.HasValue) rows = rows.Where(r => r.CreatedAt >= from.Value).ToList();
        if (to.HasValue) rows = rows.Where(r => r.CreatedAt <= to.Value).ToList();

        return MarketplaceResult<List<MarketplaceTransactionDto>>.Ok(rows);
    }

    // ==========================================================
    // analytics (read-only)
    // ==========================================================

    /// <summary>Sales over time, by category, by account type and best sellers.</summary>
    public async Task<MarketplaceResult<MarketplaceSalesAnalyticsDto>> GetAnalyticsAsync(
        int adminId,
        int days,
        CancellationToken ct = default)
    {
        if (!await _access.IsAdminAsync(adminId, ct))
            return MarketplaceResult<MarketplaceSalesAnalyticsDto>.Forbidden(AdminOnly);

        var window = Math.Clamp(days <= 0 ? 30 : days, 1, 365);
        var from = DateTime.UtcNow.Date.AddDays(-(window - 1));

        var orders = await _db.MarketplaceOrders.AsNoTracking()
            .Where(o => o.Status == MarketplaceOrderStatus.Completed && o.CreatedAt >= from)
            .Include(o => o.Items)
            .ToListAsync(ct);

        var users = await _db.Users.AsNoTracking()
            .Where(u => orders.Select(o => o.UserId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        // Order items snapshot the product NAME but not its category, so the
        // product -> category map is loaded once here rather than queried per
        // line. Local to this request, so it is safe under concurrency.
        var categoryByProduct = await _db.MarketplaceProducts.AsNoTracking()
            .Select(p => new { p.ProductId, p.Category })
            .ToDictionaryAsync(x => x.ProductId, x => x.Category, ct);

        string CategoryOf(int? productId) =>
            productId is int id && categoryByProduct.TryGetValue(id, out var category)
                ? category
                : "Removed product";

        // ---- over time ----
        var overTime = orders
            .GroupBy(o => o.CreatedAt.Date)
            .OrderBy(g => g.Key)
            .Select(g => new MarketplaceSalesBucketDto
            {
                Day = g.Key.ToString("yyyy-MM-dd"),
                AmountCentavos = g.Sum(o => o.TotalCentavos),
                Orders = g.Count()
            })
            .ToList();

        // ---- by category ----
        var byCategory = orders
            .SelectMany(o => o.Items.Select(i => new { Order = o, Item = i, Category = CategoryOf(i.ProductId) }))
            .GroupBy(x => x.Category)
            .Select(g => new MarketplaceSalesGroupDto
            {
                Label = g.Key,
                AmountCentavos = g.Sum(x => x.Item.SubtotalCentavos),
                Orders = g.Select(x => x.Order.OrderId).Distinct().Count()
            })
            .OrderByDescending(g => g.AmountCentavos)
            .ToList();

        // ---- student vs faculty ----
        var byAccountType = orders
            .GroupBy(o => users.GetValueOrDefault(o.UserId)?.Role ?? "Unknown")
            .Select(g => new MarketplaceSalesGroupDto
            {
                Label = g.Key,
                AmountCentavos = g.Sum(o => o.TotalCentavos),
                Orders = g.Count()
            })
            .OrderByDescending(g => g.AmountCentavos)
            .ToList();

        // ---- best sellers ----
        var bestSellers = orders
            .SelectMany(o => o.Items)
            .GroupBy(i => new { i.ProductId, i.ProductName })
            .Select(g => new MarketplaceBestSellerDto
            {
                ProductId = g.Key.ProductId ?? 0,
                Name = g.Key.ProductName,
                Category = CategoryOf(g.Key.ProductId),
                UnitsSold = g.Sum(i => i.Quantity),
                AmountCentavos = g.Sum(i => i.SubtotalCentavos)
            })
            .OrderByDescending(b => b.AmountCentavos)
            .Take(10)
            .ToList();

        return MarketplaceResult<MarketplaceSalesAnalyticsDto>.Ok(new MarketplaceSalesAnalyticsDto
        {
            TotalSalesCentavos = orders.Sum(o => o.TotalCentavos),
            OverTime = overTime,
            ByCategory = byCategory,
            ByAccountType = byAccountType,
            BestSellers = bestSellers
        });
    }

    private const int TransactionLimit = 500;

    private const string AdminOnly = "CDM OneServe Admin access required.";
}