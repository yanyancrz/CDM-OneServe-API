using System.Globalization;

using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Marketplace;
using CDM_OneServe_API.Services.Notifications;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Orders: the single ordering system that serves BOTH fulfillment methods.
///
///   Pickup         : Pending -> Confirmed -> Preparing -> ReadyForPickup -> Completed
///   CampusDelivery : Pending -> Confirmed -> Preparing -> OutForDelivery -> Delivered -> Completed
///   Either way, Cancelled is reachable until the order is handed over.
///
/// Same cart, same checkout, same order table, same inventory, same staff
/// dashboard for both - only the path after checkout differs.
///
/// Inventory follows the order:
///   place    -> reserve (TryReserveAsync, the oversell guard)
///   cancel   -> release (the shelf keeps the units, only the hold goes)
///   complete -> consume (units leave the shelf for good)
/// </summary>
public class MarketplaceOrderService
{
    private readonly AppDbContext _db;
    private readonly MarketplaceAccessService _access;
    private readonly MarketplaceInventoryService _inventory;
    private readonly MarketplaceCatalogService _catalog;
    private readonly IPushDispatcher _push;

    public MarketplaceOrderService(
        AppDbContext db,
        MarketplaceAccessService access,
        MarketplaceInventoryService inventory,
        MarketplaceCatalogService catalog,
        IPushDispatcher push)
    {
        _db = db;
        _access = access;
        _inventory = inventory;
        _catalog = catalog;
        _push = push;
    }

    // ==========================================================
    // WEB PUSH
    // ==========================================================
    // Marketplace orders had no notification channel at all. These are
    // browser pushes only (the module has no in-app notification table),
    // so the order reference always lands in the message text - that is
    // all the student needs to act on it.
    // ==========================================================

    private async Task PushOrderAsync(
        int userId,
        string type,
        string title,
        string body,
        string dedupeKey,
        CancellationToken ct)
    {
        try
        {
            await _push.EnqueueAsync(new PushNotification(
                UserId: userId,
                Module: "Marketplace",
                Type: type,
                Title: title,
                Body: body,
                TargetUrl: PushRoutes.Resolve("Marketplace", type),
                DedupeKey: dedupeKey), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A push failure must never fail a marketplace order.
        }
    }

    /// <summary>Everyone on the Marketplace staff roster, heads included.</summary>
    private async Task<List<int>> ActiveStaffUserIdsAsync(CancellationToken ct) =>
        await _db.MarketplaceStaff.AsNoTracking()
            .Where(s => s.IsActive)
            .Select(s => s.UserId)
            .Distinct()
            .ToListAsync(ct);

    // ==========================================================
    // BUYER
    // ==========================================================

    /// <summary>
    /// Turns the buyer's cart into one order and reserves the stock.
    ///
    /// Everything money-related is re-derived here, never taken from the client:
    /// unit prices come from the product rows, and the delivery fee comes from
    /// marketplace_settings (staff-configured, never hardcoded, and always 0 for
    /// Pickup).
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceOrderDto>> PlaceOrderAsync(
        int userId,
        PlaceMarketplaceOrderRequest request,
        CancellationToken ct = default)
    {
        var buyer = await _access.RequireBuyerAsync(userId, ct);
        if (buyer.Actor is null) return MarketplaceAccessService.BuyerRefusal<MarketplaceOrderDto>(buyer);

        // ---------------------------------------------------------------
        // IDEMPOTENCY FIRST, before anything else is validated.
        //
        // This must come before the cart is read. Placing an order empties the
        // cart, so a retried request - the classic double-tap, or a network
        // timeout where the response never arrived - would otherwise come back
        // as "Your cart is empty" and the buyer would reasonably conclude the
        // order failed. With the lookup first, the retry returns the ORIGINAL
        // order and there is still exactly one order.
        // ---------------------------------------------------------------

        var requestKey = request.RequestKey == Guid.Empty ? Guid.NewGuid() : request.RequestKey;

        var existing = await _db.MarketplaceOrders
            .Include(o => o.CampusLocation)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.UserId == userId && o.IdempotencyKey == requestKey, ct);

        if (existing is not null)
            return MarketplaceResult<MarketplaceOrderDto>.Ok(ToDto(existing, buyer.Actor.User));

        var method = MarketplaceFulfillment.Normalize(request.FulfillmentMethod);
        var paymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "cash" : request.PaymentMethod;

        if (paymentMethod is not ("cash" or "online"))
            return MarketplaceResult<MarketplaceOrderDto>.BadRequest("Choose a valid payment method.");

        // ----- the cart -----
        var cart = await _db.MarketplaceCartItems
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.CartItemId)
            .ToListAsync(ct);

        if (cart.Count == 0)
            return MarketplaceResult<MarketplaceOrderDto>.BadRequest("Your cart is empty.");

        var productIds = cart.Select(c => c.ProductId).Distinct().ToList();
        var variantIds = cart.Where(c => c.VariantId.HasValue).Select(c => c.VariantId!.Value).ToList();

        var products = await _db.MarketplaceProducts
            .Where(p => productIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId, ct);

        var variants = await _db.MarketplaceProductVariants
            .Where(v => variantIds.Contains(v.VariantId))
            .ToDictionaryAsync(v => v.VariantId, ct);

        // ----- validate every line and price it from the database -----
        var lines = new List<(MarketplaceCartItem Cart, string Name, string VariantName, int Unit, int Subtotal)>();

        foreach (var item in cart)
        {
            if (!products.TryGetValue(item.ProductId, out var product) || !product.IsActive)
                return MarketplaceResult<MarketplaceOrderDto>.BadRequest(
                    $"\"{(item.ProductId)}\" is no longer available. Refresh your cart.");

            var unit = product.PriceCentavos;
            var variantName = "";

            if (item.VariantId is int variantId)
            {
                if (!variants.TryGetValue(variantId, out var variant) || !variant.IsActive)
                    return MarketplaceResult<MarketplaceOrderDto>.BadRequest(
                        "A selected size is no longer available. Refresh your cart.");

                unit += variant.PriceAdjustmentCentavos;
                variantName = variant.VariantValue;
            }
            else if (product.HasVariants)
            {
                return MarketplaceResult<MarketplaceOrderDto>.BadRequest(
                    $"Choose a size for \"{product.Name}\".");
            }

            lines.Add((item, product.Name, variantName, unit, unit * item.Quantity));
        }

        var subtotal = lines.Sum(l => l.Subtotal);

        // ----- campus delivery: location required, fee from settings -----
        var settings = await _catalog.LoadSettingsAsync(ct);
        int deliveryFee = 0;

        if (method == MarketplaceFulfillment.CampusDelivery)
        {
            if (!settings.DeliveryEnabled)
                return MarketplaceResult<MarketplaceOrderDto>.BadRequest(
                    "Campus Delivery is not available right now. Please choose Pick Up.");

            var recipient = (request.RecipientName ?? "").Trim();
            var contact = (request.ContactNumber ?? "").Trim();
            var details = (request.SpecificLocation ?? "").Trim();

            if (recipient.Length < 2)
                return MarketplaceResult<MarketplaceOrderDto>.BadRequest("Enter the recipient name.");

            if (contact.Length < 7)
                return MarketplaceResult<MarketplaceOrderDto>.BadRequest("Enter a contact number.");

            if (request.CampusLocationId is not int locationId)
                return MarketplaceResult<MarketplaceOrderDto>.BadRequest(
                    "Choose an approved campus location.");

            // The location must exist AND be active. This is what stops a buyer
            // naming an off-campus address: there is nowhere else to point.
            var location = await _db.MarketplaceCampusLocations
                .FirstOrDefaultAsync(l => l.LocationId == locationId && l.IsActive, ct);

            if (location is null)
                return MarketplaceResult<MarketplaceOrderDto>.BadRequest(
                    "That campus location is not available.");

            if (location.AllowsDetails && details.Length < 2)
                return MarketplaceResult<MarketplaceOrderDto>.BadRequest(
                    $"Add the specific room, office or area within {location.LocationName}.");

            deliveryFee = settings.DeliveryFeeCentavos;
        }

        var total = subtotal + deliveryFee;

        // ----- everything below is one atomic unit -----
        var order = new MarketplaceOrder
        {
            UserId = userId,
            IdempotencyKey = requestKey,
            FulfillmentMethod = method,
            PaymentMethod = paymentMethod,
            Status = MarketplaceOrderStatus.Pending,
            PaymentStatus = "unpaid",
            SubtotalCentavos = subtotal,
            DeliveryFeeCentavos = deliveryFee,
            TotalCentavos = total,
            Notes = (request.Notes ?? "").Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        if (method == MarketplaceFulfillment.CampusDelivery)
        {
            order.RecipientName = (request.RecipientName ?? "").Trim();
            order.ContactNumber = (request.ContactNumber ?? "").Trim();
            order.CampusLocationId = request.CampusLocationId;
            order.SpecificLocation = (request.SpecificLocation ?? "").Trim();
            order.DeliveryInstructions = (request.DeliveryInstructions ?? "").Trim();
        }

        foreach (var (cartItem, name, variantName, unit, lineSubtotal) in lines)
        {
            order.Items.Add(new MarketplaceOrderItem
            {
                ProductId = cartItem.ProductId,
                VariantId = cartItem.VariantId,
                // Snapshots: the history stays accurate after a rename or reprice.
                ProductName = name,
                VariantName = variantName,
                Quantity = cartItem.Quantity,
                UnitPriceCentavos = unit,
                SubtotalCentavos = lineSubtotal
            });
        }

        // The reference is allocated BEFORE the row is inserted, so the order is
        // written exactly once with its final reference. See NextReferenceAsync
        // for why the order of these two statements matters.
        order.OrderReference = await NextReferenceAsync(order.CreatedAt, ct);

        _db.MarketplaceOrders.Add(order);
        await _db.SaveChangesAsync(ct);

        // ----- reserve stock, aborting the whole order if anything runs short -----
        var reserved = new List<(int ProductId, int? VariantId, int Quantity)>();

        foreach (var (cartItem, _, _, _, _) in lines)
        {
            var ok = await _inventory.TryReserveAsync(
                cartItem.ProductId, cartItem.VariantId, cartItem.Quantity, ct);

            if (!ok)
            {
                // Put back everything already taken, then drop the order. The
                // buyer keeps their cart untouched so they can adjust and retry.
                foreach (var (productId, variantId, quantity) in reserved)
                    await _inventory.ReleaseAsync(productId, variantId, quantity, ct);

                _db.MarketplaceOrders.Remove(order);
                await _db.SaveChangesAsync(ct);

                return MarketplaceResult<MarketplaceOrderDto>.BadRequest(
                    $"\"{products[cartItem.ProductId].Name}\" just sold out. " +
                    "Please review your cart.");
            }

            reserved.Add((cartItem.ProductId, cartItem.VariantId, cartItem.Quantity));
        }

        // ----- the cart has become an order -----
        _db.MarketplaceCartItems.RemoveRange(cart);
        await _db.SaveChangesAsync(ct);

        var placed = await _db.MarketplaceOrders
            .Include(o => o.CampusLocation)
            .Include(o => o.Items)
            .FirstAsync(o => o.OrderId == order.OrderId, ct);

        // Buyer: "we got it."
        await PushOrderAsync(
            order.UserId,
            "order-status",
            "Order placed",
            $"Order {order.OrderReference} was placed. We will notify you when it is ready.",
            $"orderplaced:{order.OrderId}",
            ct);

        // Staff: a new order is waiting.
        var staffIds = await ActiveStaffUserIdsAsync(ct);
        foreach (var staffId in staffIds)
        {
            if (staffId == order.UserId) continue;

            await PushOrderAsync(
                staffId,
                "order-placed",
                "New Marketplace order",
                $"{order.OrderReference} was just placed ({MarketplaceFulfillment.Label(order.FulfillmentMethod)}).",
                $"neworder:{order.OrderId}:{staffId}",
                ct);
        }

        return MarketplaceResult<MarketplaceOrderDto>.Ok(
            ToDto(placed, buyer.Actor.User), "Order placed.");
    }

    /// <summary>The buyer's own orders, newest first. Never anybody else's.</summary>
    public async Task<MarketplaceResult<List<MarketplaceOrderDto>>> GetMyOrdersAsync(
        int userId,
        CancellationToken ct = default)
    {
        var buyer = await _access.RequireBuyerAsync(userId, ct);
        if (buyer.Actor is null) return MarketplaceAccessService.BuyerRefusal<List<MarketplaceOrderDto>>(buyer);

        var orders = await _db.MarketplaceOrders.AsNoTracking()
            .Where(o => o.UserId == userId)
            .Include(o => o.CampusLocation)
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.OrderId)
            .ToListAsync(ct);

        return MarketplaceResult<List<MarketplaceOrderDto>>.Ok(
            orders.Select(o => ToDto(o, buyer.Actor.User)).ToList());
    }

    /// <summary>
    /// One of the buyer's orders. Ownership is checked against the database, so
    /// swapping an OrderId in the request returns 404 rather than somebody
    /// else's data.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceOrderDto>> GetMyOrderAsync(
        int userId,
        int orderId,
        CancellationToken ct = default)
    {
        var buyer = await _access.RequireBuyerAsync(userId, ct);
        if (buyer.Actor is null) return MarketplaceAccessService.BuyerRefusal<MarketplaceOrderDto>(buyer);

        var order = await _db.MarketplaceOrders.AsNoTracking()
            .Where(o => o.OrderId == orderId && o.UserId == userId)
            .Include(o => o.CampusLocation)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(ct);

        if (order is null)
            return MarketplaceResult<MarketplaceOrderDto>.NotFound("Order not found.");

        return MarketplaceResult<MarketplaceOrderDto>.Ok(ToDto(order, buyer.Actor.User));
    }

    /// <summary>
    /// Buyer-initiated cancellation. Releases the reservation so the units go
    /// straight back on the shelf, and records the cancellation.
    /// </summary>
    public async Task<MarketplaceResult<object>> CancelMyOrderAsync(
        int userId,
        int orderId,
        CancelMarketplaceOrderRequest request,
        CancellationToken ct = default)
    {
        var buyer = await _access.RequireBuyerAsync(userId, ct);
        if (buyer.Actor is null) return MarketplaceAccessService.BuyerRefusal<object>(buyer);

        var order = await _db.MarketplaceOrders
            .Where(o => o.OrderId == orderId && o.UserId == userId)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(ct);

        if (order is null)
            return MarketplaceResult<object>.NotFound("Order not found.");

        if (order.Status == MarketplaceOrderStatus.Cancelled)
            return MarketplaceResult<object>.Ok(new { }, "This order is already cancelled.");

        if (!MarketplaceOrderStatus.CanCancel(order.Status))
            return MarketplaceResult<object>.BadRequest(
                "This order can no longer be cancelled. Please contact Marketplace Staff.");

        await ApplyCancellationAsync(order, (request.Reason ?? "").Trim(), ct);

        return MarketplaceResult<object>.Ok(new { }, "Order cancelled.");
    }

    // ==========================================================
    // STAFF
    // ==========================================================

    /// <summary>
    /// Every order, for the Staff Orders / Campus Deliveries / Transactions
    /// screens. Pickup and CampusDelivery rows sit in the same list - the
    /// Fulfillment column is what separates them.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceOrderDto>>> ListForStaffAsync(
        int staffId,
        string? status,
        string? method,
        string? search,
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<List<MarketplaceOrderDto>>.Forbidden(StaffOnly);

        var orders = await _db.MarketplaceOrders.AsNoTracking()
            .Include(o => o.CampusLocation)
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.OrderId)
            .Take(OrderTableLimit)
            .ToListAsync(ct);

        var buyerIds = orders.Select(o => o.UserId).Distinct().ToList();
        var buyers = await _db.Users.AsNoTracking()
            .Where(u => buyerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        var rows = orders.Select(o => ToDto(o, buyers.GetValueOrDefault(o.UserId))).ToList();

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
                (r.CampusLocationName ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                (r.RecipientName ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return MarketplaceResult<List<MarketplaceOrderDto>>.Ok(rows);
    }

    /// <summary>Only Campus Delivery orders, for the dedicated Deliveries screen.</summary>
    public async Task<MarketplaceResult<List<MarketplaceOrderDto>>> ListDeliveriesAsync(
        int staffId,
        string? status,
        CancellationToken ct = default)
    {
        var all = await ListForStaffAsync(staffId, status, MarketplaceFulfillment.CampusDelivery, null, null, null, ct);
        if (!all.Success) return all;

        return MarketplaceResult<List<MarketplaceOrderDto>>.Ok(all.Data!
            .OrderByDescending(o => o.CreatedAt)
            .ToList());
    }

    public async Task<MarketplaceResult<MarketplaceOrderDto>> GetForStaffAsync(
        int staffId,
        int orderId,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<MarketplaceOrderDto>.Forbidden(StaffOnly);

        var order = await _db.MarketplaceOrders.AsNoTracking()
            .Where(o => o.OrderId == orderId)
            .Include(o => o.CampusLocation)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(ct);

        if (order is null)
            return MarketplaceResult<MarketplaceOrderDto>.NotFound("Order not found.");

        var buyer = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == order.UserId, ct);
        return MarketplaceResult<MarketplaceOrderDto>.Ok(ToDto(order, buyer));
    }

    /// <summary>
    /// Advances an order exactly one step along ITS OWN fulfillment path.
    ///
    /// The next status is computed here, never taken from the request, so a Pick
    /// Up order can never be pushed down the delivery path and vice versa, and a
    /// staff member cannot skip a step.
    /// </summary>
    public async Task<MarketplaceResult<object>> UpdateStatusAsync(
        int staffId,
        int orderId,
        string status,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<object>.Forbidden(StaffOnly);

        var order = await _db.MarketplaceOrders
            .Where(o => o.OrderId == orderId)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(ct);

        if (order is null)
            return MarketplaceResult<object>.NotFound("Order not found.");

        // Staff may also cancel directly, while it is still cancellable.
        if (string.Equals(status, MarketplaceOrderStatus.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            if (!MarketplaceOrderStatus.CanCancel(order.Status))
                return MarketplaceResult<object>.BadRequest("This order can no longer be cancelled.");

            await ApplyCancellationAsync(order, "Cancelled by Marketplace Staff.", ct);
            return MarketplaceResult<object>.Ok(new { }, "Order cancelled.");
        }

        var next = MarketplaceOrderStatus.Next(order.FulfillmentMethod, order.Status);

        if (next is null)
            return MarketplaceResult<object>.BadRequest("This order has already reached its final step.");

        if (!string.Equals(status, next, StringComparison.OrdinalIgnoreCase))
            return MarketplaceResult<object>.BadRequest(
                $"The next step for this {MarketplaceFulfillment.Label(order.FulfillmentMethod)} " +
                $"order is \"{next}\".");

        // Completed is the only step that moves stock for good.
        if (next == MarketplaceOrderStatus.Completed && order.PaymentStatus == "unpaid")
        {
            // Campus is cash-on-hand, so completing implies the money changed hands.
            order.PaymentStatus = "paid";
        }

        order.Status = next;
        order.UpdatedAt = DateTime.UtcNow;

        var statusTitle = next switch
        {
            MarketplaceOrderStatus.Completed => "Order completed",
            MarketplaceOrderStatus.Delivered => "Order delivered",
            MarketplaceOrderStatus.OutForDelivery => "Out for delivery",
            MarketplaceOrderStatus.ReadyForPickup => "Ready for pickup",
            _ => $"Order {next}"
        };

        await PushOrderAsync(
            order.UserId,
            "order-status",
            statusTitle,
            $"Order {order.OrderReference} is now {next}.",
            $"orderstatus:{order.OrderId}:{next}",
            ct);

        if (next == MarketplaceOrderStatus.Completed)
        {
            order.CompletedAt = DateTime.UtcNow;

            foreach (var item in order.Items)
            {
                if (item.ProductId is not int productId) continue;
                await _inventory.ConsumeAsync(productId, item.VariantId, item.Quantity, ct);
            }
        }

        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<object>.Ok(new { }, $"Order is now {next}.");
    }

    // ==========================================================
    // internal
    // ==========================================================

    /// <summary>
    /// Cancels an order and gives the stock back. Shared by the buyer and staff
    /// paths so both release inventory identically.
    /// </summary>
    private async Task ApplyCancellationAsync(
        MarketplaceOrder order,
        string reason,
        CancellationToken ct)
    {
        order.Status = MarketplaceOrderStatus.Cancelled;
        order.CancelledAt = DateTime.UtcNow;
        order.UpdatedAt = DateTime.UtcNow;

        // A cancelled order must never continue to fulfillment.
        order.PaymentStatus = order.PaymentStatus == "paid" ? "paid" : "unpaid";

        foreach (var item in order.Items)
        {
            if (item.ProductId is not int productId) continue;
            await _inventory.ReleaseAsync(productId, item.VariantId, item.Quantity, ct);
        }

        // Both the buyer and the staff path cancel through here, so the
        // buyer is told exactly once, whoever pressed the button.
        await PushOrderAsync(
            order.UserId,
            "order-cancelled",
            "Order cancelled",
            $"Order {order.OrderReference} was cancelled.",
            $"ordercancelled:{order.OrderId}",
            ct);

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// ORD-20261008-0001 - a daily sequence, so references stay readable and
    /// sortable by date.
    ///
    /// WHY THIS IS NOT JUST "COUNT(*) + 1"
    /// Counting existing orders is a race: two buyers pressing "Place order" in
    /// the same second both read the same count, generate the same reference, and
    /// one fails on the UNIQUE OrderReference index. That is not theoretical - it
    /// was reproduced with two concurrent buyers fighting over the last unit.
    ///
    /// WHY READING THE ROW BACK IS ALSO NOT ENOUGH
    /// Incrementing the counter is atomic, but SELECTing it afterwards is not:
    /// the other transaction can bump it again before the read lands, so both
    /// callers can see the same number. The increment and the read must return
    /// the SAME value.
    ///
    /// THE FIX
    /// MySQL/MariaDB's LAST_INSERT_ID(expr) sets a per-connection value as part
    /// of the statement itself, in both the insert and the update branch. Reading
    /// LAST_INSERT_ID() immediately afterwards on the SAME connection therefore
    /// returns exactly the number this call reserved - never another caller's.
    /// The row lock the upsert takes serialises concurrent orders behind it.
    ///
    /// That is what allows the reference to be assigned BEFORE the order row is
    /// written, so the order is inserted exactly once.
    /// </summary>
    private async Task<string> NextReferenceAsync(DateTime createdAt, CancellationToken ct)
    {
        var day = createdAt.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var date = createdAt.Date;

        // One connection and one command for BOTH statements - that is what makes
        // LAST_INSERT_ID() meaningful. Two separate DbContext round trips could
        // land on different pooled connections.
        var connection = _db.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;

        if (openedHere) await connection.OpenAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO marketplace_order_counters (`DayDate`, `LastNumber`)
                VALUES (@day, LAST_INSERT_ID(1))
                ON DUPLICATE KEY UPDATE `LastNumber` = LAST_INSERT_ID(`LastNumber` + 1);
                """;

            var dayParam = command.CreateParameter();
            dayParam.ParameterName = "@day";
            dayParam.Value = date;
            command.Parameters.Add(dayParam);

            await command.ExecuteNonQueryAsync(ct);

            command.Parameters.Clear();
            command.CommandText = "SELECT LAST_INSERT_ID();";

            var allocated = await command.ExecuteScalarAsync(ct);
            var number = Convert.ToInt32(allocated);

            // Unreachable while the table is healthy; a guard rather than a
            // fallback, because an empty reference would violate UNIQUE.
            if (number < 1)
                throw new InvalidOperationException(
                    $"Could not allocate an order reference number for {day}.");

            return $"ORD-{day}-{number:D4}";
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    private static MarketplaceOrderDto ToDto(MarketplaceOrder order, User? buyer)
    {
        var flow = MarketplaceOrderStatus.Flow(order.FulfillmentMethod).ToList();
        var offTrack = order.Status is MarketplaceOrderStatus.Cancelled
            or MarketplaceOrderStatus.Completed;

        var currentIndex = flow.IndexOf(order.Status);
        var done = currentIndex < 0 ? new List<string>() : flow.Take(currentIndex + 1).ToList();

        var next = MarketplaceOrderStatus.Next(order.FulfillmentMethod, order.Status);

        return new MarketplaceOrderDto
        {
            OrderId = order.OrderId,
            OrderReference = order.OrderReference,
            UserId = order.UserId,
            BuyerName = buyer?.FullName ?? "",
            BuyerAccountType = buyer?.Role ?? "",

            FulfillmentMethod = order.FulfillmentMethod,
            FulfillmentLabel = MarketplaceFulfillment.Label(order.FulfillmentMethod),

            RecipientName = order.RecipientName,
            ContactNumber = order.ContactNumber,
            CampusLocationId = order.CampusLocationId,
            CampusLocationName = order.CampusLocation?.LocationName,
            SpecificLocation = order.SpecificLocation,
            DeliveryInstructions = order.DeliveryInstructions,

            Notes = order.Notes,
            PaymentMethod = order.PaymentMethod,

            SubtotalCentavos = order.SubtotalCentavos,
            DeliveryFeeCentavos = order.DeliveryFeeCentavos,
            TotalCentavos = order.TotalCentavos,

            Status = order.Status,
            PaymentStatus = order.PaymentStatus,

            Timeline = flow,
            TimelineDone = done,
            TimelineStep = currentIndex < 0 ? 0 : currentIndex + 1,
            NextStatus = offTrack ? null : next,
            NextStatusLabel = offTrack || next is null ? "" : next,

            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            CompletedAt = order.CompletedAt,
            CancelledAt = order.CancelledAt,

            Items = order.Items
                .OrderBy(i => i.OrderItemId)
                .Select(i => new MarketplaceOrderItemDto
                {
                    OrderItemId = i.OrderItemId,
                    ProductId = i.ProductId,
                    ProductName = i.ProductName,
                    VariantId = i.VariantId,
                    VariantName = i.VariantName,
                    Quantity = i.Quantity,
                    UnitPriceCentavos = i.UnitPriceCentavos,
                    SubtotalCentavos = i.SubtotalCentavos
                })
                .ToList()
        };
    }

    /// <summary>How many orders the staff table pulls in one read.</summary>
    private const int OrderTableLimit = 500;

    private const string StaffOnly = "Marketplace Staff access required.";
}
