using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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
    private readonly MarketplaceOperatorSessionService _sessions;
    private readonly MarketplaceAuditService _audit;
    private readonly MarketplaceCardService _cards;
    private readonly MarketplaceClaimService _claims;
    private readonly MarketplaceEmailOutboxService _outbox;
    private readonly IPushDispatcher _push;

    public MarketplaceOrderService(
        AppDbContext db,
        MarketplaceAccessService access,
        MarketplaceInventoryService inventory,
        MarketplaceCatalogService catalog,
        MarketplaceOperatorSessionService sessions,
        MarketplaceAuditService audit,
        MarketplaceCardService cards,
        MarketplaceClaimService claims,
        MarketplaceEmailOutboxService outbox,
        IPushDispatcher push)
    {
        _db = db;
        _access = access;
        _inventory = inventory;
        _catalog = catalog;
        _sessions = sessions;
        _audit = audit;
        _cards = cards;
        _claims = claims;
        _outbox = outbox;
        _push = push;
    }

    // ==========================================================
    // UNPAID / UNCOLLECTED EXPIRY
    // ==========================================================

    /// <summary>
    /// Expires every order that has been sitting at the counter past its
    /// collect-by deadline, and gives their stock back.
    ///
    /// WHAT THIS IS FOR
    ///   This is the fix for a real inventory leak. A buyer could place a cash
    ///   order and never collect it, and the hold on
    ///   marketplace_inventory.ReservedQuantity was never returned - there was no
    ///   code path at all that touched it for an uncollected order. Worse,
    ///   MarketplaceOrderStatus.CanCancel forbids cancelling once an order reaches
    ///   ReadyForPickup, so even a diligent member of staff could not free the
    ///   units. The leaked hold is self-reinforcing: it trips the guard on
    ///   MarketplaceInventoryService.SaveAsync ("N unit(s) are reserved by open
    ///   orders. Cancel or complete those orders first."), so it then blocks the
    ///   stock from being managed at all.
    ///
    /// WHY THE DEADLINE IS 24 HOURS AND NOT THE ORIGINAL'S 10 MINUTES
    ///   CampusMarket expires an `awaiting_payment` order after ten minutes,
    ///   which is right there because that status means an unfinished ONLINE
    ///   payment. Here a cash order is `unpaid` from placement until it is
    ///   collected at the counter, which is hours - so a ten-minute rule would
    ///   cancel almost every cash order before the kitchen had started it.
    ///
    ///   The deadline therefore starts when the order BECOMES COLLECTABLE, not
    ///   when it is placed. Before that, staff are actively preparing it and it is
    ///   not neglected.
    ///
    /// WHAT IS SAFE TO EXPIRE
    ///   Only orders that are both collectable AND past their deadline. Orders
    ///   created before 010_order_lifecycle.sql have ExpiresAt NULL, so they are
    ///   left alone: backdating a deadline onto a live order would cancel it
    ///   with no warning and no chance to react.
    ///
    /// Returns the number of orders expired, so the job can log and the test can
    /// assert without re-querying.
    /// </summary>
    public async Task<int> ExpireOverdueOrdersAsync(
        DateTime nowUtc,
        int? workspaceId = null,
        CancellationToken ct = default)
    {
        // Only the two "waiting at the counter" steps can be abandoned. Everything
        // before them is still being prepared, so expiring it would cancel orders
        // the kitchen has already started.
        //
        // The deadline is read here rather than in SQL so the rule stays in one
        // place - MarketplaceOrderExpiry - and the query stays a plain index seek
        // on IX_Order_Expiry.
        // Index seek on IX_Order_Expiry: only orders that have a deadline, only
        // those still holding a reservation.
        var query = _db.MarketplaceOrders
            .Where(o => !o.StockSettled
                        && o.ExpiresAt != null
                        && o.ExpiresAt < nowUtc
                        && MarketplaceOrderExpiry.CollectableStatuses.Contains(o.Status));

        if (workspaceId.HasValue)
            query = query.Where(o => o.WorkspaceId == workspaceId.Value);

        // Include the lines up front so the per-product release can run without a
        // second round-trip per order.
        var orders = await query
            .Include(o => o.Items)
            .ToListAsync(ct);

        if (orders.Count == 0) return 0;

        var expired = 0;

        foreach (var order in orders)
        {
            // Re-check under the lock. Between the query and now, a concurrent
            // claim may have settled this order - it must not be expired after it
            // was collected, and it must not be expired twice.
            if (order.StockSettled) continue;

            if (order.ExpiresAt is not { } deadline || deadline >= nowUtc) continue;

            // Claim first, then act. Losing the race is normal - a double tap, or
            // a worker and staff acting together - and the winner has already
            // written the truth.
            if (!await _inventory.TryClaimStockSettleAsync(order.OrderId, ct)) continue;

            order.StockSettled = true;
            order.Status = MarketplaceOrderStatus.Expired;
            order.CancelledAt = DateTime.UtcNow;
            order.ExpiryReason = MarketplaceOrderExpiry.UncollectedReason;
            order.UpdatedAt = DateTime.UtcNow;

            // No actor: this is a scheduled expiry, not a person's action. The
            // audit row records the movement with a NULL actor rather than a
            // fabricated user id - see 011_system_audit_actor.sql.
            await _inventory.ReleaseOrderLinesAsync(
                order, actorUserId: null, operatorName: null, idLast3: null,
                MarketplaceOrderExpiry.UncollectedReason, "Expired", ct);

            expired++;

            await PushOrderAsync(
                order.UserId,
                "order-expired",
                "Order expired",
                $"Order {order.OrderReference} expired: {MarketplaceOrderExpiry.UncollectedReason}.",
                $"orderexpired:{order.OrderId}",
                ct);
        }

        if (expired > 0)
            await _db.SaveChangesAsync(ct);

        return expired;
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

    /// <summary>
    /// Staff recipients for one workspace: its stall account(s), floating
    /// operators (legacy personal staff with no fixed stall), plus heads.
    /// </summary>
    private async Task<List<int>> ActiveWorkspaceStaffUserIdsAsync(
        int workspaceId, CancellationToken ct)
    {
        var stallUsers = await _db.MarketplaceStallAccounts.AsNoTracking()
            .Where(a => a.WorkspaceId == workspaceId && a.IsActive)
            .Select(a => a.UserId)
            .ToListAsync(ct);

        var linkedUserIds = await _db.MarketplaceStallAccounts.AsNoTracking()
            .Where(a => a.IsActive)
            .Select(a => a.UserId)
            .ToListAsync(ct);

        var floating = await _db.MarketplaceStaff.AsNoTracking()
            .Where(s => s.IsActive && !linkedUserIds.Contains(s.UserId))
            .Select(s => s.UserId)
            .ToListAsync(ct);

        var heads = await _db.Users.AsNoTracking()
            .Where(u => u.AccountStatus == "Active" &&
                        u.Role == MarketplaceAccessService.HeadRole &&
                        u.AdminModule == MarketplaceAccessService.HeadModule)
            .Select(u => u.Id)
            .ToListAsync(ct);

        return stallUsers.Concat(floating).Concat(heads).Distinct().ToList();
    }

    // ==========================================================
    // BUYER
    // ==========================================================

    /// <summary>
    /// Turns the buyer's cart into ONE ORDER PER WORKSPACE and reserves stock.
    ///
    /// Stalls fulfil independently, so a cart spanning Stall 1 and the
    /// BusinessHub becomes two orders with two references. A single-stall
    /// cart returns exactly one order, as before.
    ///
    /// Everything money-related is re-derived here, never taken from the client:
    /// unit prices come from the product rows, and the delivery fee comes from
    /// marketplace_settings (staff-configured, never hardcoded, and always 0 for
    /// Pickup). A multi-stall delivery checkout carries the fee once per order,
    /// because each stall delivers on its own.
    /// </summary>
    public async Task<MarketplaceResult<MarketplacePlacedOrdersDto>> PlaceOrderAsync(
        int userId,
        PlaceMarketplaceOrderRequest request,
        CancellationToken ct = default)
    {
        var buyer = await _access.RequireBuyerAsync(userId, ct);
        if (buyer.Actor is null) return MarketplaceAccessService.BuyerRefusal<MarketplacePlacedOrdersDto>(buyer);

        var requestKey = request.RequestKey == Guid.Empty ? Guid.NewGuid() : request.RequestKey;

        // ---------------------------------------------------------------
        // IDEMPOTENCY FIRST, before the cart is read.
        //
        // Placing an order empties the cart, so a retried checkout - the
        // classic double-tap, or a timeout where the response never arrived -
        // would otherwise come back as "Your cart is empty". Every order one
        // checkout produced carries the original key, so the retry returns
        // the ORIGINAL group and there is still exactly one order per stall.
        // ---------------------------------------------------------------

        if (request.RequestKey != Guid.Empty)
        {
            var replayed = await _db.MarketplaceOrders
                .Where(o => o.UserId == userId && o.RequestKey == request.RequestKey)
                .Include(o => o.CampusLocation)
                .Include(o => o.ClaimedByUser)
                .Include(o => o.Workspace)
                .Include(o => o.Items)
                .OrderBy(o => o.OrderId)
                .ToListAsync(ct);

            if (replayed.Count > 0)
                return MarketplaceResult<MarketplacePlacedOrdersDto>.Ok(
                    new MarketplacePlacedOrdersDto
                    {
                        Orders = replayed.Select(o => ToDto(o, buyer.Actor.User)).ToList()
                    },
                    replayed.Count > 1
                        ? $"{replayed.Count} orders placed - one per stall."
                        : "Order placed.");
        }

        var method = MarketplaceFulfillment.Normalize(request.FulfillmentMethod);
        var paymentMethod = MarketplacePaymentMethod.Normalize(request.PaymentMethod);

        // "online" was the placeholder the original column was reserved for.
        // The only alternative to cash is now the campus card, so a client still
        // sending "online" is a 400 rather than a silently unpaid order.
        if (request.PaymentMethod is not null
            && request.PaymentMethod.Trim().Length > 0
            && !MarketplacePaymentMethod.IsValid(request.PaymentMethod))
            return MarketplaceResult<MarketplacePlacedOrdersDto>.BadRequest("Choose a valid payment method.");

        // ===== pre-ordering (003_campus_services.sql) =====
        // A booked slot only makes sense for pickup, so a slot sent with a
        // delivery method is dropped rather than silently accepted - a meal
        // arriving at a room cannot honour a 12:00 slot.
        var isPreOrder = method == MarketplacePreOrder.FulfillmentMethod && request.ScheduledFor.HasValue;
        var scheduledFor = isPreOrder ? request.ScheduledFor : null;

        var slotError = MarketplacePreOrder.Validate(scheduledFor, DateTime.UtcNow);
        if (slotError is not null)
            return MarketplaceResult<MarketplacePlacedOrdersDto>.BadRequest(slotError);

        // ----- the cart -----
        var cart = await _db.MarketplaceCartItems
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.CartItemId)
            .ToListAsync(ct);

        if (cart.Count == 0)
            return MarketplaceResult<MarketplacePlacedOrdersDto>.BadRequest("Your cart is empty.");

        var productIds = cart.Select(c => c.ProductId).Distinct().ToList();
        var variantIds = cart.Where(c => c.VariantId.HasValue).Select(c => c.VariantId!.Value).ToList();

        var products = await _db.MarketplaceProducts
            .Where(p => productIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId, ct);

        var neededWorkspaceIds = products.Values
            .Where(p => p.WorkspaceId.HasValue)
            .Select(p => p.WorkspaceId!.Value)
            .Distinct()
            .ToList();

        var workspaces = await _db.MarketplaceWorkspaces.AsNoTracking()
            .Where(w => neededWorkspaceIds.Contains(w.WorkspaceId))
            .ToDictionaryAsync(w => w.WorkspaceId, ct);

        var variants = await _db.MarketplaceProductVariants
            .Where(v => variantIds.Contains(v.VariantId))
            .ToDictionaryAsync(v => v.VariantId, ct);

        // ----- validate every line and price it from the database -----
        var lines = new List<(MarketplaceCartItem Cart, string Name, string VariantName, int Unit, int Subtotal, int WorkspaceId)>();

        foreach (var item in cart)
        {
            if (!products.TryGetValue(item.ProductId, out var product) || !product.IsActive)
                return MarketplaceResult<MarketplacePlacedOrdersDto>.BadRequest(
                    $"\"{(item.ProductId)}\" is no longer available. Refresh your cart.");

            // Every product belongs to a workspace, and an inactive stall
            // sells nothing - even though its rows stay for history.
            if (!product.WorkspaceId.HasValue ||
                !workspaces.TryGetValue(product.WorkspaceId.Value, out var workspace) ||
                !workspace.IsActive)
                return MarketplaceResult<MarketplacePlacedOrdersDto>.BadRequest(
                    $"\"{product.Name}\" is not available right now. Refresh your cart.");

            var unit = product.PriceCentavos;
            var variantName = "";

            if (item.VariantId is int variantId)
            {
                if (!variants.TryGetValue(variantId, out var variant) || !variant.IsActive)
                    return MarketplaceResult<MarketplacePlacedOrdersDto>.BadRequest(
                        "A selected size is no longer available. Refresh your cart.");

                unit += variant.PriceAdjustmentCentavos;
                variantName = variant.VariantValue;
            }
            else if (product.HasVariants)
            {
                return MarketplaceResult<MarketplacePlacedOrdersDto>.BadRequest(
                    $"Choose a size for \"{product.Name}\".");
            }

            lines.Add((item, product.Name, variantName, unit, unit * item.Quantity, product.WorkspaceId!.Value));
        }

        // ----- campus delivery: location required, fee from settings -----
        var settings = await _catalog.LoadSettingsAsync(ct);
        int deliveryFee = 0;

        if (method == MarketplaceFulfillment.CampusDelivery)
        {
            if (!settings.DeliveryEnabled)
                return MarketplaceResult<MarketplacePlacedOrdersDto>.BadRequest(
                    "Campus Delivery is not available right now. Please choose Pick Up.");

            var recipient = (request.RecipientName ?? "").Trim();
            var contact = (request.ContactNumber ?? "").Trim();
            var details = (request.SpecificLocation ?? "").Trim();

            if (recipient.Length < 2)
                return MarketplaceResult<MarketplacePlacedOrdersDto>.BadRequest("Enter the recipient name.");

            if (contact.Length < 7)
                return MarketplaceResult<MarketplacePlacedOrdersDto>.BadRequest("Enter a contact number.");

            if (request.CampusLocationId is not int locationId)
                return MarketplaceResult<MarketplacePlacedOrdersDto>.BadRequest(
                    "Choose an approved campus location.");

            // The location must exist AND be active. This is what stops a buyer
            // naming an off-campus address: there is nowhere else to point.
            var location = await _db.MarketplaceCampusLocations
                .FirstOrDefaultAsync(l => l.LocationId == locationId && l.IsActive, ct);

            if (location is null)
                return MarketplaceResult<MarketplacePlacedOrdersDto>.BadRequest(
                    "That campus location is not available.");

            if (location.AllowsDetails && details.Length < 2)
                return MarketplaceResult<MarketplacePlacedOrdersDto>.BadRequest(
                    $"Add the specific room, office or area within {location.LocationName}.");

            deliveryFee = settings.DeliveryFeeCentavos;
        }

        // ----- one order per workspace -----
        //
        // IDEMPOTENCY PER GROUP, before anything is written. Retries (double-tap,
        // timeout with no response) return the ORIGINAL group orders: groups
        // already placed are collected, only missing ones are created. The group
        // key is derived deterministically from the client's key, so a retry
        // with the same key replays the same groups.
        var placedOrders = new List<MarketplaceOrder>();
        var createdThisCall = new List<MarketplaceOrder>();

        var groups = lines
            .GroupBy(l => l.WorkspaceId)
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var group in groups)
        {
            var groupKey = GroupIdempotencyKey(requestKey, group.Key);

            var replay = await _db.MarketplaceOrders
                .Where(o => o.UserId == userId && o.IdempotencyKey == groupKey)
                .Include(o => o.Items)
                .FirstOrDefaultAsync(ct);

            if (replay is not null)
            {
                placedOrders.Add(replay);
                continue;
            }

            var groupSubtotal = group.Sum(l => l.Subtotal);

            // ----- everything below is one atomic unit -----
            var order = new MarketplaceOrder
            {
                UserId = userId,
                WorkspaceId = group.Key,
                IdempotencyKey = groupKey,
                RequestKey = request.RequestKey == Guid.Empty ? null : request.RequestKey,
                FulfillmentMethod = method,
                PaymentMethod = paymentMethod,
                IsPreOrder = isPreOrder,
                ScheduledFor = scheduledFor,
                Status = MarketplaceOrderStatus.Pending,
                // A campus card order is settled further down this method, once
                // the stock is safely reserved - so it starts unpaid like the
                // rest and is only marked paid after the debit succeeds.
                PaymentStatus = "unpaid",
                SubtotalCentavos = groupSubtotal,
                DeliveryFeeCentavos = deliveryFee,
                TotalCentavos = groupSubtotal + deliveryFee,
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

            foreach (var (cartItem, name, variantName, unit, lineSubtotal, _) in group)
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

            // ----- reserve stock, aborting everything if anything runs short -----
            var reserved = new List<(int ProductId, int? VariantId, int Quantity)>();
            var shortProduct = "";

            foreach (var (cartItem, _, _, _, _, _) in group)
            {
                var audit = new MarketplaceStockAudit(
                    group.Key, userId, OperatorName: null, IdLast3: null, order.OrderId, Reason: null);

                var ok = await _inventory.TryReserveAsync(
                    cartItem.ProductId, cartItem.VariantId, cartItem.Quantity, audit, ct);

                if (!ok)
                {
                    shortProduct = products[cartItem.ProductId].Name;
                    break;
                }

                reserved.Add((cartItem.ProductId, cartItem.VariantId, cartItem.Quantity));
            }

            if (shortProduct.Length > 0)
            {
                // Put back this group's takes, then roll back EVERY group this
                // call created (replayed groups from an earlier call are left
                // alone). The buyer keeps their cart untouched.
                foreach (var (productId, variantId, quantity) in reserved)
                    await _inventory.ReleaseAsync(productId, variantId, quantity, null, ct);

                foreach (var created in createdThisCall)
                {
                    foreach (var item in created.Items)
                    {
                        if (item.ProductId is not int productId) continue;
                        await _inventory.ReleaseAsync(
                            productId, item.VariantId, item.Quantity, null, ct);
                    }

                    _db.MarketplaceOrders.Remove(created);
                }

                _db.MarketplaceOrders.Remove(order);
                await _db.SaveChangesAsync(ct);

                return MarketplaceResult<MarketplacePlacedOrdersDto>.BadRequest(
                    $"\"{shortProduct}\" just sold out. Please review your cart.");
            }

            // ----- settle a campus card order, now that the stock is safe -----
            // Only here: before this point the order is rolled back on failure,
            // and after it the buyer owns a real order. Debiting earlier would
            // take the money for an order that might not survive.
            if (paymentMethod == MarketplacePaymentMethod.CampusCard)
            {
                var charge = await _cards.DebitForOrderAsync(order, ct);

                if (!charge.Success)
                {
                    // Same rollback as a stock failure: give the stock back and
                    // delete every order this call created, including this one.
                    foreach (var (productId, variantId, quantity) in reserved)
                        await _inventory.ReleaseAsync(productId, variantId, quantity, null, ct);

                    foreach (var created in createdThisCall)
                    {
                        foreach (var item in created.Items)
                        {
                            if (item.ProductId is not int productId) continue;
                            await _inventory.ReleaseAsync(
                                productId, item.VariantId, item.Quantity, null, ct);
                        }

                        _db.MarketplaceOrders.Remove(created);
                    }

                    _db.MarketplaceOrders.Remove(order);
                    await _db.SaveChangesAsync(ct);

                    return MarketplaceResult<MarketplacePlacedOrdersDto>.BadRequest(
                        charge.Message ?? "Your campus card could not be charged.");
                }

                order.PaymentStatus = "paid";
                await _db.SaveChangesAsync(ct);

                // Queue the receipt email. The outbox worker sends it with retries,
                // so a transient SMTP failure never loses the receipt.
                await _outbox.QueueReceiptAsync(order.OrderId, ct);
            }

            // ----- issue a claim code for anything collected at the counter -----
            // Cash-on-pickup and every pre-order are handed over in person, so
            // both need a code. Delivery already proves identity by room, and a
            // card order is already paid, so neither does.
            var needsClaimCode =
                paymentMethod != MarketplacePaymentMethod.CampusCard
                && (isPreOrder || method == MarketplaceFulfillment.Pickup);

            if (needsClaimCode)
                order.ClaimCode = await _claims.NextClaimCodeAsync(ct);

            placedOrders.Add(order);
            createdThisCall.Add(order);
        }

        // ----- the cart has become order(s) -----
        _db.MarketplaceCartItems.RemoveRange(cart);
        await _db.SaveChangesAsync(ct);

        var orderedIds = placedOrders.Select(o => o.OrderId).ToList();

        var placed = await _db.MarketplaceOrders
            .Where(o => orderedIds.Contains(o.OrderId))
            .Include(o => o.CampusLocation)
            .Include(o => o.ClaimedByUser)
            .Include(o => o.Workspace)
            .Include(o => o.Items)
            .OrderBy(o => o.OrderId)
            .ToListAsync(ct);

        foreach (var order in placed)
        {
            // Buyer: "we got it."
            await PushOrderAsync(
                order.UserId,
                "order-status",
                "Order placed",
                $"Order {order.OrderReference} was placed. We will notify you when it is ready.",
                $"orderplaced:{order.OrderId}",
                ct);

            // The stall that fulfils it: a new order is waiting.
            var staffIds = await ActiveWorkspaceStaffUserIdsAsync(order.WorkspaceId ?? 0, ct);
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
        }

        return MarketplaceResult<MarketplacePlacedOrdersDto>.Ok(
            new MarketplacePlacedOrdersDto
            {
                Orders = placed.Select(o => ToDto(o, buyer.Actor.User)).ToList()
            },
            placed.Count > 1
                ? $"{placed.Count} orders placed - one per stall."
                : "Order placed.");
    }

    /// <summary>
    /// Deterministic per-workspace idempotency key, so a retried checkout
    /// replays the same group orders instead of duplicating them.
    /// </summary>
    private static Guid GroupIdempotencyKey(Guid requestKey, int workspaceId)
    {
        var bytes = MD5.HashData(
            Encoding.UTF8.GetBytes($"{requestKey:N}:{workspaceId}"));

        return new Guid(bytes);
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
            .Include(o => o.ClaimedByUser)
            .Include(o => o.Workspace)
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
            .Include(o => o.ClaimedByUser)
            .Include(o => o.Workspace)
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

        await ApplyCancellationAsync(
            order, (request.Reason ?? "").Trim(), userId, operatorName: null, idLast3: null, ct);

        return MarketplaceResult<object>.Ok(new { }, "Order cancelled.");
    }

    // ==========================================================
    // STAFF
    // ==========================================================

    /// <summary>
    /// Every order, for the Staff Orders / Campus Deliveries / Transactions
    /// screens. Pickup and CampusDelivery rows sit in the same list - the
    /// Fulfillment column is what separates them.
    ///
    /// Stall accounts see their own workspace only. Floating operators see
    /// their session workspace, or everything when browsing without one.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceOrderDto>>> ListForStaffAsync(
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
        if (actor is null) return MarketplaceResult<List<MarketplaceOrderDto>>.Forbidden(StaffOnly);

        var (scope, scopeError) = await _sessions.ResolveReadScopeAsync(
            staffId, sessionId, workspaceId, ct);

        if (scopeError is not null)
            return MarketplaceResult<List<MarketplaceOrderDto>>.Forbidden(scopeError);

        var orders = await _db.MarketplaceOrders.AsNoTracking()
            .Where(o => scope == null || o.WorkspaceId == scope)
            .Include(o => o.CampusLocation)
            .Include(o => o.ClaimedByUser)
            .Include(o => o.Workspace)
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
        Guid? sessionId,
        int? workspaceId,
        CancellationToken ct = default)
    {
        var all = await ListForStaffAsync(
            staffId, status, MarketplaceFulfillment.CampusDelivery, null, null, null,
            sessionId, workspaceId, ct);
        if (!all.Success) return all;

        return MarketplaceResult<List<MarketplaceOrderDto>>.Ok(all.Data!
            .OrderByDescending(o => o.CreatedAt)
            .ToList());
    }

    public async Task<MarketplaceResult<MarketplaceOrderDto>> GetForStaffAsync(
        int staffId,
        int orderId,
        Guid? sessionId,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<MarketplaceOrderDto>.Forbidden(StaffOnly);

        var (scope, scopeError) = await _sessions.ResolveReadScopeAsync(
            staffId, sessionId, requestedWorkspaceId: null, ct);

        if (scopeError is not null)
            return MarketplaceResult<MarketplaceOrderDto>.Forbidden(scopeError);

        var order = await _db.MarketplaceOrders.AsNoTracking()
            .Where(o => o.OrderId == orderId &&
                        (scope == null || o.WorkspaceId == scope))
            .Include(o => o.CampusLocation)
            .Include(o => o.ClaimedByUser)
            .Include(o => o.Workspace)
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
    ///
    /// Requires an operator session: the entered name is recorded on the
    /// completion's stock movement, and the order must belong to the
    /// session's workspace.
    /// </summary>
    public async Task<MarketplaceResult<object>> UpdateStatusAsync(
        int staffId,
        int orderId,
        string status,
        Guid? sessionId,
        CancellationToken ct = default)
    {
        var (attribution, scopeError) = await _sessions.RequireMutationScopeAsync(
            staffId, sessionId, targetWorkspaceId: null, ct);

        if (attribution is null)
            return MarketplaceResult<object>.Forbidden(scopeError ?? StaffOnly);

        var order = await _db.MarketplaceOrders
            .Where(o => o.OrderId == orderId)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(ct);

        if (order is null)
            return MarketplaceResult<object>.NotFound("Order not found.");

        if (order.WorkspaceId != attribution.WorkspaceId)
            return MarketplaceResult<object>.Forbidden("That order belongs to another workspace.");

        // Staff may also cancel directly, while it is still cancellable.
        if (string.Equals(status, MarketplaceOrderStatus.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            if (!MarketplaceOrderStatus.CanCancel(order.Status))
                return MarketplaceResult<object>.BadRequest("This order can no longer be cancelled.");

            await ApplyCancellationAsync(
                order, "Cancelled by Marketplace Staff.", staffId, attribution.OperatorName, attribution.IdLast3, ct);
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
            order.PaidAt = DateTime.UtcNow;

            // Queue the receipt so the buyer gets proof of payment.
            await _outbox.QueueReceiptAsync(order.OrderId, ct);
        }

        // The collect-by deadline starts HERE, not at placement.
        //
        // A cash order is `unpaid` from the moment it is placed until the buyer
        // collects it at the counter - hours, not minutes. So the deadline cannot
        // be measured from placement without cancelling nearly every cash order
        // before the kitchen has started it. It is measured from the moment the
        // order is actually sitting at the counter waiting, which is the only
        // point at which it can be abandoned.
        //
        // Only set when there is not already one: an order that was already
        // collectable before 010_order_lifecycle.sql has ExpiresAt NULL, and
        // backdating a deadline onto a live order would cancel it with no warning.
        if (order.ExpiresAt is null &&
            MarketplaceOrderExpiry.IsCollectable(next))
        {
            order.ExpiresAt = DateTime.UtcNow.Add(MarketplaceOrderExpiry.CollectableWindow);
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

            // Same lock as cancellation. Completing CONSUMES the hold rather than
            // returning it, but it settles the order just as finally - so a
            // completed order and a claim-released order must not both be able to
            // consume the same reservation.
            if (await _inventory.TryClaimStockSettleAsync(order.OrderId, ct))
            {
                order.StockSettled = true;

                foreach (var item in order.Items)
                {
                    if (item.ProductId is not int productId) continue;

                    var audit = new MarketplaceStockAudit(
                        order.WorkspaceId ?? attribution.WorkspaceId, staffId,
                        attribution.OperatorName, attribution.IdLast3, order.OrderId, Reason: null);

                    await _inventory.ConsumeAsync(productId, item.VariantId, item.Quantity, audit, ct);
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<object>.Ok(new { }, $"Order is now {next}.");
    }

    // ==========================================================
    // CASH PAYMENT CONFIRMATION
    // ==========================================================

    /// <summary>
    /// Records that cash was actually taken for an order.
    ///
    /// WHY THIS IS A SEPARATE STEP
    ///   Before, an order became paid only as a side effect of something else:
    ///   either the claim code was released at the counter (ClaimService), or the
    ///   order was walked to Completed. Both of those are moments when money
    ///   changes hands, so the inference was usually right - but it meant there
    ///   was no way to record a payment on its own, and therefore no audit trail
    ///   answering "when was this cash received, and by whom".
    ///
    ///   Splitting it out gives three things:
    ///     * the counter can take cash for an order that is not yet ready;
    ///     * PaidAt is stamped at the moment of payment, not at hand-over;
    ///     * a cash order can be paid without being released in the same step.
    ///
    /// WHAT IT REFUSES
    ///   Only a cash order that is still unpaid. A campus card order was debited
    ///   at checkout and has its own ledger, so confirming it here would double
    ///   count the money. An already-paid order is refused rather than
    ///   re-stamped, because overwriting PaidAt would move the one timestamp that
    ///   says when the money changed hands.
    ///
    /// It does NOT touch stock: payment and settlement are separate facts, and
    /// the hold is released or consumed exactly once by the StockSettled lock.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceOrderDto>> ConfirmCashAsync(
        int staffId,
        int orderId,
        Guid? sessionId,
        CancellationToken ct = default)
    {
        // A mutation, so the same operator-session rule as every other staff
        // write: the duty shift is what makes "who took the cash" answerable.
        var (attribution, scopeError) = await _sessions.RequireMutationScopeAsync(
            staffId, sessionId, targetWorkspaceId: null, ct);

        if (attribution is null)
            return MarketplaceResult<MarketplaceOrderDto>.Forbidden(scopeError ?? StaffOnly);

        var order = await _db.MarketplaceOrders
            .Include(o => o.User)
            .FirstOrDefaultAsync(o => o.OrderId == orderId, ct);

        if (order is null)
            return MarketplaceResult<MarketplaceOrderDto>.NotFound("Order not found.");

        if (order.WorkspaceId != attribution.WorkspaceId)
            return MarketplaceResult<MarketplaceOrderDto>.Forbidden(
                "That order belongs to another workspace.");

        if (order.PaymentMethod == MarketplacePaymentMethod.CampusCard)
            return MarketplaceResult<MarketplaceOrderDto>.BadRequest(
                "That order was paid with a campus card. It cannot be confirmed as cash.");

        if (order.PaymentStatus == "paid")
            return MarketplaceResult<MarketplaceOrderDto>.Conflict(
                "Cash for this order was already confirmed.");

        // A cancelled or expired order is not collectable, so taking money for
        // it would be taking money for nothing.
        if (order.Status is MarketplaceOrderStatus.Cancelled
            or MarketplaceOrderStatus.Expired
            or MarketplaceOrderStatus.Completed)
            return MarketplaceResult<MarketplaceOrderDto>.BadRequest(
                $"This order is {order.Status}, so it cannot be paid for.");

        order.PaymentStatus = "paid";
        order.PaidAt = DateTime.UtcNow;
        order.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _outbox.QueueReceiptAsync(order.OrderId, ct);

        await PushOrderAsync(
            order.UserId,
            "order-cash-confirmed",
            "Cash received",
            $"Cash for order {order.OrderReference} was confirmed at the counter.",
            $"ordercash:{order.OrderId}",
            ct);

        return MarketplaceResult<MarketplaceOrderDto>.Ok(
            ToDto(order, order.User),
            $"Cash confirmed for {order.OrderReference}.");
    }

    // ==========================================================
    // internal
    // ==========================================================

    /// <summary>
    /// Cancels an order and gives the stock back. Shared by the buyer and staff
    /// paths so both release inventory identically. Every released line is
    /// audited with whoever cancelled and, for staff, their operator name.
    /// </summary>
    private async Task ApplyCancellationAsync(
        MarketplaceOrder order,
        string reason,
        int actorUserId,
        string? operatorName,
        string? idLast3,
        CancellationToken ct)
    {
        // Claim the order's stock settle FIRST, before writing anything.
        //
        // This conditional UPDATE is the lock. It used to be the other way round -
        // the order row was mutated and the inventory decremented, and only then
        // was SaveChangesAsync called. ExecuteUpdateAsync writes immediately, so
        // a crash in that window left the stock released but the order still
        // open, and the retry released a second time.
        //
        // Losing the race is normal and expected: a buyer and a member of staff
        // cancelling together, or a double tap. The caller then leaves the order
        // row alone - the winner already wrote the truth.
        if (!await _inventory.TryClaimStockSettleAsync(order.OrderId, ct)) return;

        order.StockSettled = true;

        order.Status = MarketplaceOrderStatus.Cancelled;
        order.CancelledAt = DateTime.UtcNow;
        order.UpdatedAt = DateTime.UtcNow;

        // A cancelled order must never continue to fulfillment.
        order.PaymentStatus = order.PaymentStatus == "paid" ? "paid" : "unpaid";

        foreach (var item in order.Items)
        {
            if (item.ProductId is not int productId) continue;

            // Post-migration every order carries its workspace; the lookup is
            // only a backstop so an audit row can never break a cancellation.
            var workspaceId = order.WorkspaceId ?? await _db.MarketplaceProducts
                .Where(p => p.ProductId == productId)
                .Select(p => p.WorkspaceId)
                .FirstOrDefaultAsync(ct) ?? 0;

            MarketplaceStockAudit? audit = workspaceId == 0 ? null : new MarketplaceStockAudit(
                workspaceId, actorUserId, operatorName, idLast3, order.OrderId, reason);

            await _inventory.ReleaseAsync(
                productId, item.VariantId, item.Quantity, audit, ct, "Cancellation");
        }

        // A campus card order was debited at checkout, so cancelling has to give
        // the money back - otherwise the student pays for an order that no
        // longer exists. RefundOrderAsync is idempotent: it only credits when the
        // order actually has a Debit on its ledger, so cancelling twice (buyer
        // and staff racing) cannot pay out twice.
        if (order.PaymentMethod == MarketplacePaymentMethod.CampusCard)
        {
            var refund = await _cards.RefundOrderAsync(order, ct);

            if (refund.Success)
            {
                order.PaymentStatus = "unpaid";
                await PushOrderAsync(
                    order.UserId,
                    "order-refunded",
                    "Campus card refunded",
                    $"PHP {order.TotalCentavos / 100.0:0.00} was returned to your campus card for {order.OrderReference}.",
                    $"orderrefunded:{order.OrderId}",
                    ct);
            }
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
            PaymentMethodLabel = MarketplacePaymentMethod.Label(order.PaymentMethod),

            IsPreOrder = order.IsPreOrder,
            ScheduledFor = order.ScheduledFor,
            ClaimCode = order.ClaimCode,
            IsClaimed = order.ClaimedAt.HasValue,
            ClaimedAt = order.ClaimedAt,
            ClaimedByName = order.ClaimedByUser?.FullName ?? "",

            WorkspaceId = order.WorkspaceId,
            WorkspaceName = order.Workspace?.Name ?? "",

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
