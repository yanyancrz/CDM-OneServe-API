using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// The single Marketplace Staff dashboard, and the Staff Transactions table.
///
/// Both are read-only aggregations over tables the staff already owns. Stall
/// accounts see their own workspace only; floating operators see their
/// session workspace, or everything when browsing without one.
///
/// Note this is NOT the same as the CDM OneServe Admin's monitoring view: that one
/// is a read-only outsider's view, while this one sits beside the controls staff
/// actually operate.
/// </summary>
public class MarketplaceStaffDashboardService
{
    private readonly AppDbContext _db;
    private readonly MarketplaceAccessService _access;
    private readonly MarketplaceOperatorSessionService _sessions;

    public MarketplaceStaffDashboardService(
        AppDbContext db,
        MarketplaceAccessService access,
        MarketplaceOperatorSessionService sessions)
    {
        _db = db;
        _access = access;
        _sessions = sessions;
    }

    /// <summary>
    /// Everything the Staff Dashboard shows in one read: order counts per step,
    /// stock warnings, sales windows, the fulfillment split and chat activity.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceStaffDashboardDto>> GetDashboardAsync(
        int staffId,
        Guid? sessionId,
        int? workspaceId,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<MarketplaceStaffDashboardDto>.Forbidden(StaffOnly);

        var (scope, scopeError) = await _sessions.ResolveReadScopeAsync(
            staffId, sessionId, workspaceId, ct);

        if (scopeError is not null)
            return MarketplaceResult<MarketplaceStaffDashboardDto>.Forbidden(scopeError);

        var today = DateTime.UtcNow.Date;
        var week = today.AddDays(-7);
        var month = today.AddDays(-30);

        var orders = await _db.MarketplaceOrders.AsNoTracking()
            .Where(o => scope == null || o.WorkspaceId == scope)
            .ToListAsync(ct);
        var completed = orders.Where(o => o.Status == MarketplaceOrderStatus.Completed).ToList();

        var productIdsInScope = scope == null
            ? null
            : await _db.MarketplaceProducts.AsNoTracking()
                .Where(p => p.WorkspaceId == scope)
                .Select(p => p.ProductId)
                .ToListAsync(ct);

        var inventory = await _db.MarketplaceInventory.AsNoTracking()
            .Where(i => productIdsInScope == null || productIdsInScope.Contains(i.ProductId))
            .ToListAsync(ct);

        // Only active products count as a product, otherwise a retired item would
        // keep appearing in the staff's own product count.
        var activeProductIds = await _db.MarketplaceProducts.AsNoTracking()
            .Where(p => p.IsActive && (productIdsInScope == null || productIdsInScope.Contains(p.ProductId)))
            .Select(p => p.ProductId)
            .ToListAsync(ct);

        var liveInventory = inventory
            .Where(i => activeProductIds.Contains(i.ProductId))
            .ToList();

        int Count(string status) => orders.Count(o => o.Status == status);

        var unread = await _db.MarketplaceMessages.AsNoTracking()
            .CountAsync(m => m.SenderType == MarketplaceSender.User && !m.IsRead, ct);

        var open = await _db.MarketplaceConversations.AsNoTracking()
            .CountAsync(c => c.Status != MarketplaceChatStatus.Resolved, ct);

        return MarketplaceResult<MarketplaceStaffDashboardDto>.Ok(new MarketplaceStaffDashboardDto
        {
            PendingOrders = Count(MarketplaceOrderStatus.Pending),
            ConfirmedOrders = Count(MarketplaceOrderStatus.Confirmed),
            PreparingOrders = Count(MarketplaceOrderStatus.Preparing),
            ReadyForPickupOrders = Count(MarketplaceOrderStatus.ReadyForPickup),
            OutForDeliveryOrders = Count(MarketplaceOrderStatus.OutForDelivery),
            DeliveredOrders = Count(MarketplaceOrderStatus.Delivered),
            CompletedOrders = Count(MarketplaceOrderStatus.Completed),
            CancelledOrders = Count(MarketplaceOrderStatus.Cancelled),

            TotalProducts = activeProductIds.Count,
            LowStock = liveInventory.Count(i =>
                i.StockQuantity - i.ReservedQuantity > 0 &&
                i.StockQuantity - i.ReservedQuantity <= i.LowStockThreshold),
            OutOfStock = liveInventory.Count(i => i.StockQuantity - i.ReservedQuantity <= 0),

            // Revenue is completed orders only - a cancelled or in-flight order is
            // not money in hand yet.
            TodaySalesCentavos = completed.Where(o => o.CompletedAt >= today).Sum(o => (long)o.TotalCentavos),
            WeekSalesCentavos = completed.Where(o => o.CompletedAt >= week).Sum(o => (long)o.TotalCentavos),
            MonthSalesCentavos = completed.Where(o => o.CompletedAt >= month).Sum(o => (long)o.TotalCentavos),
            TotalSalesCentavos = completed.Sum(o => (long)o.TotalCentavos),

            PickupOrders = orders.Count(o => o.FulfillmentMethod == MarketplaceFulfillment.Pickup),
            CampusDeliveryOrders = orders.Count(o => o.FulfillmentMethod == MarketplaceFulfillment.CampusDelivery),

            OpenConversations = open,
            UnreadMessages = unread
        });
    }

    /// <summary>
    /// The Staff Transactions table. Same shape the Admin monitoring view returns,
    /// so the two screens stay comparable.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceTransactionDto>>> ListTransactionsAsync(
        int staffId,
        string? status,
        string? method,
        string? search,
        DateTime? from,
        DateTime? to,
        Guid? sessionId,
        int? workspaceId,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<List<MarketplaceTransactionDto>>.Forbidden(StaffOnly);

        var (scope, scopeError) = await _sessions.ResolveReadScopeAsync(
            staffId, sessionId, workspaceId, ct);

        if (scopeError is not null)
            return MarketplaceResult<List<MarketplaceTransactionDto>>.Forbidden(scopeError);

        var orders = await _db.MarketplaceOrders.AsNoTracking()
            .Where(o => scope == null || o.WorkspaceId == scope)
            .Include(o => o.CampusLocation)
            .Include(o => o.Workspace)
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.OrderId)
            .Take(Limit)
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
                WorkspaceId = o.WorkspaceId,
                WorkspaceName = o.Workspace?.Name ?? "",
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

        if (from.HasValue) rows = rows.Where(r => r.CreatedAt >= from.Value).ToList();
        if (to.HasValue) rows = rows.Where(r => r.CreatedAt <= to.Value).ToList();

        var needle = (search ?? "").Trim();
        if (needle.Length > 0)
        {
            rows = rows.Where(r =>
                r.OrderReference.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                r.BuyerName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                (r.RecipientName ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return MarketplaceResult<List<MarketplaceTransactionDto>>.Ok(rows);
    }

    private const int Limit = 500;

    private const string StaffOnly = "Marketplace Staff access required.";
}