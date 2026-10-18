using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Marketplace;

using Microsoft.EntityFrameworkCore;
namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Pre-ordering and claim verification, added by
/// Database/Marketplace/003_campus_services.sql.
///
/// WHAT THIS IS
///   * A pre-order names a pickup slot the buyer booked, so the canteen batches
///     cooking instead of starting when you walk in.
///   * Every collectable order gets a short CLAIM CODE. The buyer shows it at the
///     counter and staff release the order with it.
///
/// WHY THE CODE EXISTS
///   Order references are long and sequential (ORD-20261017-0042). Reading one
///   aloud across a counter is how the wrong order gets handed to the wrong
///   person. The claim code is 8 unambiguous characters and is checked against
///   the order, not just shown.
///
/// RULES
///   * A claim code is only released for an order that is actually collectable -
///     never for a cancelled one, and never for an order that is already
///     handed over.
///   * Releasing an order consumes the reserved stock and completes it, exactly
///     like the normal Completed transition, so there is one place where stock
///     is consumed rather than two.
///   * Staff mutations require a live operator session, like every other write
///     in this module.
/// </summary>
public class MarketplaceClaimService
{
    private readonly AppDbContext _db;
    private readonly MarketplaceAccessService _access;
    private readonly MarketplaceInventoryService _inventory;
    private readonly MarketplaceOperatorSessionService _sessions;

    /// <summary>
    /// Characters used for claim codes. 0/O and 1/I/L are deliberately absent:
    /// a code that can be misread aloud is a code that hands over the wrong
    /// order.
    /// </summary>
    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    public MarketplaceClaimService(
        AppDbContext db,
        MarketplaceAccessService access,
        MarketplaceInventoryService inventory,
        MarketplaceOperatorSessionService sessions)
    {
        _db = db;
        _access = access;
        _inventory = inventory;
        _sessions = sessions;
    }

    // =====================================================
    // BUYER
    // =====================================================

    /// <summary>
    /// The pre-order window, so the buyer screen never has to hardcode how far
    /// ahead a slot may be booked.
    /// </summary>
    public async Task<MarketplaceResult<PreOrderOptionsDto>> GetOptionsAsync(
        int userId, CancellationToken ct = default)
    {
        var buyer = await _access.RequireBuyerAsync(userId, ct);
        if (buyer.Actor is null)
            return MarketplaceAccessService.BuyerRefusal<PreOrderOptionsDto>(buyer);

        var now = DateTime.UtcNow;

        return MarketplaceResult<PreOrderOptionsDto>.Ok(new PreOrderOptionsDto
        {
            Enabled = true,
            EarliestSlotUtc = now.Add(MarketplacePreOrder.MinLeadTime),
            LatestSlotUtc = now.Add(MarketplacePreOrder.MaxLeadTime),
            // 15 minutes: fine enough for "lunch at 12:00", coarse enough that
            // the picker does not offer 900 identical-looking slots.
            SlotStepMinutes = 15
        });
    }

    /// <summary>
    /// Makes sure the buyer can see the claim code for one of their own
    /// collectable orders, issuing it if it does not exist yet.
    ///
    /// The order is scoped to the caller inside the query, so asking for someone
    /// else's order id returns 404 rather than leaking that it exists.
    /// </summary>
    public async Task<MarketplaceResult<string>> EnsureClaimCodeAsync(
        int userId, int orderId, CancellationToken ct = default)
    {
        var buyer = await _access.RequireBuyerAsync(userId, ct);
        if (buyer.Actor is null)
            return MarketplaceAccessService.BuyerRefusal<string>(buyer);

        var order = await _db.MarketplaceOrders
            .FirstOrDefaultAsync(o => o.OrderId == orderId && o.UserId == userId, ct);

        if (order is null) return MarketplaceResult<string>.NotFound("That order does not exist.");

        if (!IsCollectable(order))
            return MarketplaceResult<string>.BadRequest(
                $"This order is {order.Status}, so there is nothing to claim yet.");

        if (!string.IsNullOrWhiteSpace(order.ClaimCode))
            return MarketplaceResult<string>.Ok(order.ClaimCode);

        order.ClaimCode = await NextClaimCodeAsync(ct);
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<string>.Ok(order.ClaimCode);
    }

    // =====================================================
    // STAFF (counter)
    // =====================================================

    /// <summary>
    /// Looks a claim code up at the counter.
    ///
    /// Returns the order only when the code is valid AND the order is
    /// collectable. A code that exists but belongs to a cancelled order comes
    /// back as Found=false with a reason, rather than as an error - staff need
    /// to be able to say "that was cancelled" as confidently as "no such code".
    /// </summary>
    public async Task<MarketplaceResult<ClaimLookupDto>> LookupAsync(
        int staffId, string claimCode, CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<ClaimLookupDto>.Forbidden(StaffOnly);

        var code = (claimCode ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0)
            return MarketplaceResult<ClaimLookupDto>.BadRequest("Enter the claim code.");

        var order = await _db.MarketplaceOrders.AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.User)
            .Include(o => o.CampusLocation)
            .Include(o => o.Workspace)
            .Include(o => o.ClaimedByUser)
            .FirstOrDefaultAsync(o => o.ClaimCode == code, ct);

        if (order is null)
            return MarketplaceResult<ClaimLookupDto>.NotFound("That claim code is not valid.");

        if (!IsCollectable(order))
        {
            var message = order.ClaimedAt.HasValue
                ? $"That order was already collected on {order.ClaimedAt:MMM d, yyyy h:mm tt}."
                : $"That order is {order.Status} and is not ready to collect.";

            return MarketplaceResult<ClaimLookupDto>.Ok(new ClaimLookupDto
            {
                Found = false,
                Message = message
            });
        }

        return MarketplaceResult<ClaimLookupDto>.Ok(new ClaimLookupDto
        {
            Found = true,
            Message = "Claim verified. Release the order.",
            Order = ToOrderDto(order)
        });
    }

    /// <summary>
    /// Releases a claimed order: marks it collected, completes it and consumes
    /// the reserved stock.
    ///
    /// This is the counter's "hand it over now" button. It is deliberately the
    /// same terminal state as the normal Completed transition, so stock can only
    /// ever be consumed in one place.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceOrderDto>> VerifyAsync(
        int staffId, string claimCode, string? notes, Guid? sessionId, CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<MarketplaceOrderDto>.Forbidden(StaffOnly);

        var code = (claimCode ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0) return MarketplaceResult<MarketplaceOrderDto>.BadRequest("Enter the claim code.");

        var (attribution, scopeError) = await _sessions.RequireMutationScopeAsync(
            staffId, sessionId, targetWorkspaceId: null, ct);

        if (attribution is null)
            return MarketplaceResult<MarketplaceOrderDto>.Forbidden(scopeError ?? StaffOnly);

        var order = await _db.MarketplaceOrders
            .Include(o => o.Items)
            .Include(o => o.User)
            .Include(o => o.CampusLocation)
            .Include(o => o.Workspace)
            .FirstOrDefaultAsync(o => o.ClaimCode == code, ct);

        if (order is null) return MarketplaceResult<MarketplaceOrderDto>.NotFound("That claim code is not valid.");

        if (order.ClaimedAt.HasValue)
            return MarketplaceResult<MarketplaceOrderDto>.BadRequest(
                $"That order was already collected on {order.ClaimedAt:MMM d, yyyy h:mm tt}.");

        if (!IsCollectable(order))
            return MarketplaceResult<MarketplaceOrderDto>.BadRequest(
                $"That order is {order.Status} and is not ready to collect.");

        // The stall the operator is on must be the one fulfilling the order, so
        // a canteen terminal cannot release a paper shop pickup.
        if (attribution.WorkspaceId != order.WorkspaceId)
            return MarketplaceResult<MarketplaceOrderDto>.Forbidden(
                "That order belongs to another stall.");

        var now = DateTime.UtcNow;

        order.ClaimedAt = now;
        order.ClaimedByUserId = staffId;
        order.Status = MarketplaceOrderStatus.Completed;
        order.CompletedAt = now;
        order.UpdatedAt = now;

        // Cash on pickup is settled by handing the order over, so this is where
        // it becomes paid. A card order was already debited when it was placed.
        if (order.PaymentMethod != MarketplacePaymentMethod.CampusCard)
            order.PaymentStatus = "paid";

        if (!string.IsNullOrWhiteSpace(notes))
        {
            var text = notes.Trim();
            order.Notes = order.Notes.Length > 0 ? $"{order.Notes} | Claim: {text}" : $"Claim: {text}";
        }

        // Stock is consumed here, exactly as in the normal Completed transition -
        // same helper, same audit row, so there is one place where reserved
        // stock becomes consumed.
        //
        // Guarded by the order's StockSettled lock, because releasing a claimed
        // order and walking it to Completed are two ways of settling the SAME
        // hold. Without it, a buyer whose claim code is released at the counter
        // while staff also complete it would consume the stock twice.
        if (await _inventory.TryClaimStockSettleAsync(order.OrderId, ct))
        {
            order.StockSettled = true;

            foreach (var item in order.Items)
            {
                if (item.ProductId is not int productId) continue;

                var audit = order.WorkspaceId.HasValue
                    ? new MarketplaceStockAudit(
                        order.WorkspaceId.Value, staffId, attribution.OperatorName, attribution.IdLast3,
                        order.OrderId, "Claim released")
                    : null;

                await _inventory.ConsumeAsync(
                    productId, item.VariantId, item.Quantity, audit, ct);
            }
        }

        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<MarketplaceOrderDto>.Ok(
            ToOrderDto(order), $"Claim verified. {order.OrderReference} released.");
    }

    /// <summary>
    /// Issues a fresh claim code for an order staff are looking at, for the case
    /// where the buyer's screen could not show one.
    /// </summary>
    public async Task<MarketplaceResult<string>> ReissueAsync(
        int staffId, int orderId, Guid? sessionId, CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<string>.Forbidden(StaffOnly);

        var (attribution, scopeError) = await _sessions.RequireMutationScopeAsync(
            staffId, sessionId, targetWorkspaceId: null, ct);

        if (attribution is null) return MarketplaceResult<string>.Forbidden(scopeError ?? StaffOnly);

        var order = await _db.MarketplaceOrders.FirstOrDefaultAsync(o => o.OrderId == orderId, ct);
        if (order is null) return MarketplaceResult<string>.NotFound("That order does not exist.");

        if (!IsCollectable(order))
            return MarketplaceResult<string>.BadRequest($"That order is {order.Status} and cannot be claimed.");

        order.ClaimCode = await NextClaimCodeAsync(ct);
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<string>.Ok(order.ClaimCode, "New claim code issued.");
    }

    // =====================================================
    // INTERNAL
    // =====================================================

    /// <summary>
    /// True when the order is sitting at the counter waiting to be picked up.
    /// These are exactly the two states the buyer is told to come back in.
    /// </summary>
    public static bool IsCollectable(MarketplaceOrder? order) =>
        order is not null
        && order.Status is MarketplaceOrderStatus.ReadyForPickup or MarketplaceOrderStatus.Delivered;

    /// <summary>
    /// Mints an unused 8-character order claim code. The UNIQUE index on
    /// marketplace_orders.ClaimCode is the real guard; this just makes a
    /// collision vanishingly unlikely before the insert.
    ///
    /// internal rather than private because MarketplaceOrderService issues the
    /// code at checkout, so the buyer has it from the moment the order is
    /// placed rather than having to ask for it later.
    /// </summary>
    internal async Task<string> NextClaimCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var chars = new char[8];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = Alphabet[Random.Shared.Next(Alphabet.Length)];

            var code = new string(chars);

            var taken = await _db.MarketplaceOrders.AsNoTracking().AnyAsync(o => o.ClaimCode == code, ct);
            if (!taken) return code;
        }

        throw new InvalidOperationException("Could not allocate a unique order claim code.");
    }

    /// <summary>
    /// Mirrors MarketplaceOrderService.ToDto so an order looked up at the counter
    /// renders exactly like the same order on the staff Orders screen.
    /// </summary>
    private static MarketplaceOrderDto ToOrderDto(MarketplaceOrder order)
    {
        var flow = MarketplaceOrderStatus.Flow(order.FulfillmentMethod).ToList();
        var currentIndex = flow.IndexOf(order.Status);
        var next = order.Status == MarketplaceOrderStatus.Completed
            ? null
            : MarketplaceOrderStatus.Next(order.FulfillmentMethod, order.Status);

        return new MarketplaceOrderDto
        {
            OrderId = order.OrderId,
            OrderReference = order.OrderReference,
            UserId = order.UserId,
            BuyerName = order.User?.FullName ?? "",
            BuyerAccountType = order.User?.Role ?? "",
            FulfillmentMethod = order.FulfillmentMethod,
            FulfillmentLabel = MarketplaceFulfillment.Label(order.FulfillmentMethod),
            CampusLocationName = order.CampusLocation?.LocationName ?? "",
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
            TimelineDone = currentIndex < 0 ? new List<string>() : flow.Take(currentIndex + 1).ToList(),
            TimelineStep = currentIndex < 0 ? 0 : currentIndex + 1,
            NextStatus = next,
            NextStatusLabel = next ?? "",
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            CompletedAt = order.CompletedAt,
            CancelledAt = order.CancelledAt,
            Items = order.Items.OrderBy(i => i.OrderItemId).Select(i => new MarketplaceOrderItemDto
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
    }

    private const string StaffOnly = "Marketplace Staff access required.";
}
