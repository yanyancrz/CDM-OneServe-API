using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Marketplace;

using Microsoft.EntityFrameworkCore;
namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// The campus card: issue, read, load and pay.
///
/// Created by Database/Marketplace/003_campus_services.sql.
///
/// RULES
///   * The card belongs to the EXISTING OneServe user. It never holds a name,
///     an email or a password - those are read from `users` at display time, so
///     a renamed student is reflected immediately.
///   * Money is integer centavos. The balance can never go negative, and the
///     SQL has a CHECK constraint as a second line of defence.
///   * Every movement is appended to marketplace_card_transactions with the
///     balance AFTER it, so a disputed balance can be replayed rather than
///     argued about.
///   * Card loads are a staff mutation and need a live operator session. Card
///     PAYMENT is a buyer action - it needs no operator, it debits itself.
/// </summary>
public class MarketplaceCardService
{
    private readonly AppDbContext _db;
    private readonly MarketplaceAccessService _access;
    private readonly MarketplaceOperatorSessionService _sessions;

    /// <summary>How many ledger rows the buyer sees on the card screen.</summary>
    private const int RecentTransactionLimit = 20;

    /// <summary>How many ledger rows staff see for one card at the counter.</summary>
    private const int StaffTransactionLimit = 50;

    /// <summary>Ceiling on a single top-up, in centavos (PHP 5,000).</summary>
    private const int MaxLoadCentavos = 500_000;

    public MarketplaceCardService(
        AppDbContext db,
        MarketplaceAccessService access,
        MarketplaceOperatorSessionService sessions)
    {
        _db = db;
        _access = access;
        _sessions = sessions;
    }

    // =====================================================
    // BUYER
    // =====================================================

    /// <summary>
    /// The signed-in student's or faculty member's card, with their recent
    /// ledger. Issued on first read so simply opening the screen works.
    /// </summary>
    public async Task<MarketplaceResult<CampusCardDto>> GetMyCardAsync(int userId, CancellationToken ct = default)
    {
        var buyer = await _access.RequireBuyerAsync(userId, ct);
        if (buyer.Actor is null)
            return MarketplaceAccessService.BuyerRefusal<CampusCardDto>(buyer);

        var user = buyer.Actor.User;
        var card = await _db.MarketplaceCampusCards.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);

        if (card is null) card = await IssueAsync(userId, user.IdNumber, ct);

        var recent = await _db.MarketplaceCardTransactions.AsNoTracking()
            .Where(t => t.CardId == card.CardId)
            .OrderByDescending(t => t.TransactionId)
            .Take(RecentTransactionLimit)
            .ToListAsync(ct);

        var orders = await OrdersByIdAsync(recent.Select(t => t.ReferenceOrderId).ToList(), ct);
        var performers = await UsersByIdAsync(recent.Where(t => t.PerformedByUserId.HasValue)
            .Select(t => t.PerformedByUserId!.Value).ToList(), ct);

        return MarketplaceResult<CampusCardDto>.Ok(new CampusCardDto
        {
            CardId = card.CardId,
            UserId = userId,
            FullName = user.FullName,
            IdNumber = user.IdNumber,
            AccountType = user.Role,
            CardNumber = card.CardNumber,
            BalanceCentavos = card.BalanceCentavos,
            Status = card.Status,
            IssuedAt = card.IssuedAt,
            LastLoadedAt = card.LastLoadedAt,
            RecentTransactions = recent
                .Select(t => ToTransactionDto(t, orders.GetValueOrDefault(t.ReferenceOrderId ?? 0), performers))
                .ToList()
        });
    }

    /// <summary>
    /// Spends the order total from the card balance and marks the order paid.
    ///
    /// The guard is a CONDITIONAL UPDATE, not a read-then-write: the balance is
    /// only debited when it is still high enough, so two simultaneous orders
    /// cannot both pass a balance check and overdraw the card.
    /// </summary>
    public async Task<MarketplaceResult<bool>> DebitForOrderAsync(
        MarketplaceOrder order, CancellationToken ct = default)
    {
        if (order.TotalCentavos <= 0)
            return MarketplaceResult<bool>.Ok(true, "Nothing to charge.");

        var card = await _db.MarketplaceCampusCards
            .FirstOrDefaultAsync(c => c.UserId == order.UserId, ct);

        if (card is null)
            return MarketplaceResult<bool>.BadRequest("You do not have a campus card yet. Open Campus Card to get one.");

        if (card.Status != CampusCardStatus.Active)
            return MarketplaceResult<bool>.BadRequest("Your campus card is blocked. Please load it at the counter.");

        // The oversell guard. If this affects other than 1 row, somebody else
        // spent the balance first and this order must not go through.
        var affected = await _db.MarketplaceCampusCards
            .Where(c => c.CardId == card.CardId
                        && c.Status == CampusCardStatus.Active
                        && c.BalanceCentavos >= order.TotalCentavos)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.BalanceCentavos, c => c.BalanceCentavos - order.TotalCentavos)
                .SetProperty(c => c.UpdatedAt, DateTime.UtcNow), ct);

        if (affected != 1)
        {
            var balance = await _db.MarketplaceCampusCards.AsNoTracking()
                .Where(c => c.CardId == card.CardId)
                .Select(c => c.BalanceCentavos)
                .FirstOrDefaultAsync(ct);

            return MarketplaceResult<bool>.BadRequest(
                $"Not enough campus card balance for this order. Your balance is PHP {balance / 100.0:0.00}.");
        }

        var balanceAfter = await _db.MarketplaceCampusCards.AsNoTracking()
            .Where(c => c.CardId == card.CardId)
            .Select(c => c.BalanceCentavos)
            .FirstAsync(ct);

        await _db.MarketplaceCardTransactions.AddAsync(new MarketplaceCardTransaction
        {
            CardId = card.CardId,
            UserId = order.UserId,
            Type = CampusCardTransactionType.Debit,
            AmountCentavos = order.TotalCentavos,
            BalanceAfterCentavos = balanceAfter,
            ReferenceOrderId = order.OrderId,
            Notes = $"Order {order.OrderReference}"
        }, ct);

        return MarketplaceResult<bool>.Ok(true);
    }

    /// <summary>
    /// Returns the balance a cancelled or refunded order took. Idempotent: if
    /// the order already has a Debit, it has already been given back.
    /// </summary>
    public async Task<MarketplaceResult<bool>> RefundOrderAsync(
        MarketplaceOrder order, CancellationToken ct = default)
    {
        var card = await _db.MarketplaceCampusCards
            .FirstOrDefaultAsync(c => c.UserId == order.UserId, ct);

        if (card is null || order.TotalCentavos <= 0)
            return MarketplaceResult<bool>.Ok(false);

        // A Debit for this order means it was paid by card, so a refund is due.
        var wasDebited = await _db.MarketplaceCardTransactions.AsNoTracking()
            .AnyAsync(t => t.ReferenceOrderId == order.OrderId
                           && t.Type == CampusCardTransactionType.Debit, ct);

        if (!wasDebited) return MarketplaceResult<bool>.Ok(false);

        var balanceAfter = card.BalanceCentavos + order.TotalCentavos;

        await _db.MarketplaceCampusCards
            .Where(c => c.CardId == card.CardId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.BalanceCentavos, c => c.BalanceCentavos + order.TotalCentavos)
                .SetProperty(c => c.UpdatedAt, DateTime.UtcNow), ct);

        await _db.MarketplaceCardTransactions.AddAsync(new MarketplaceCardTransaction
        {
            CardId = card.CardId,
            UserId = order.UserId,
            Type = CampusCardTransactionType.Refund,
            AmountCentavos = order.TotalCentavos,
            BalanceAfterCentavos = balanceAfter,
            ReferenceOrderId = order.OrderId,
            Notes = $"Refund for {order.OrderReference}"
        }, ct);

        return MarketplaceResult<bool>.Ok(true);
    }

    // =====================================================
    // STAFF (counter)
    // =====================================================

    /// <summary>
    /// Loads a card at the counter. Requires a live operator session so the
    /// operator name is on the ledger row and not taken from the request.
    /// </summary>
    public async Task<MarketplaceResult<CampusCardDto>> LoadAsync(
        int staffId, int targetUserId, int amountCentavos, string? notes,
        Guid? sessionId, CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null)
            return MarketplaceResult<CampusCardDto>.Forbidden(StaffOnly);

        if (amountCentavos <= 0)
            return MarketplaceResult<CampusCardDto>.BadRequest("Enter how much to load.");

        if (amountCentavos > MaxLoadCentavos)
            return MarketplaceResult<CampusCardDto>.BadRequest(
                $"A single load cannot exceed PHP {MaxLoadCentavos / 100:0}.");

        var (attribution, scopeError) = await _sessions.RequireMutationScopeAsync(
            staffId, sessionId, targetWorkspaceId: null, ct);

        if (attribution is null)
            return MarketplaceResult<CampusCardDto>.Forbidden(scopeError ?? StaffOnly);

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
        if (user is null) return MarketplaceResult<CampusCardDto>.NotFound("That account does not exist.");

        if (user.Role is not ("Student" or "Faculty"))
            return MarketplaceResult<CampusCardDto>.BadRequest("Only Student and Faculty accounts have a campus card.");

        var card = await _db.MarketplaceCampusCards.FirstOrDefaultAsync(c => c.UserId == targetUserId, ct);
        if (card is null) card = await IssueAsync(targetUserId, user.IdNumber, ct);

        if (card.Status != CampusCardStatus.Active)
            return MarketplaceResult<CampusCardDto>.BadRequest("That card is blocked. Unblock it before loading.");

        var balanceAfter = card.BalanceCentavos + amountCentavos;
        var now = DateTime.UtcNow;

        card.BalanceCentavos = balanceAfter;
        card.LastLoadedAt = now;
        card.UpdatedAt = now;

        await _db.MarketplaceCardTransactions.AddAsync(new MarketplaceCardTransaction
        {
            CardId = card.CardId,
            UserId = targetUserId,
            Type = CampusCardTransactionType.Credit,
            AmountCentavos = amountCentavos,
            BalanceAfterCentavos = balanceAfter,
            PerformedByUserId = staffId,
            OperatorName = attribution.OperatorName,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        }, ct);

        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<CampusCardDto>.Ok(new CampusCardDto
        {
            CardId = card.CardId,
            UserId = targetUserId,
            FullName = user.FullName,
            IdNumber = user.IdNumber,
            AccountType = user.Role,
            CardNumber = card.CardNumber,
            BalanceCentavos = card.BalanceCentavos,
            Status = card.Status,
            IssuedAt = card.IssuedAt,
            LastLoadedAt = card.LastLoadedAt
        }, $"Loaded PHP {amountCentavos / 100.0:0.00} to {card.CardNumber}.");
    }

    /// <summary>
    /// Looks a card up by the printed number or the owner's ID number - what
    /// staff actually type at the counter.
    /// </summary>
    public async Task<MarketplaceResult<CampusCardDto>> FindAsync(
        int staffId, string query, CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<CampusCardDto>.Forbidden(StaffOnly);

        var needle = (query ?? "").Trim();
        if (needle.Length == 0) return MarketplaceResult<CampusCardDto>.BadRequest("Enter a card number or ID number.");

        var card = await _db.MarketplaceCampusCards.AsNoTracking()
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.CardNumber == needle || c.User!.IdNumber == needle, ct);

        if (card is null)
            return MarketplaceResult<CampusCardDto>.NotFound("No campus card matches that number.");

        return MarketplaceResult<CampusCardDto>.Ok(await ToDtoAsync(card, ct));
    }

    /// <summary>Blocks or unblocks a card. Blocking keeps the balance.</summary>
    public async Task<MarketplaceResult<CampusCardDto>> SetStatusAsync(
        int staffId, int cardId, string status, CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null) return MarketplaceResult<CampusCardDto>.Forbidden(StaffOnly);

        if (!CampusCardStatus.IsValid(status))
            return MarketplaceResult<CampusCardDto>.BadRequest("Status must be Active or Blocked.");

        var card = await _db.MarketplaceCampusCards
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.CardId == cardId, ct);

        if (card is null) return MarketplaceResult<CampusCardDto>.NotFound("That card does not exist.");

        card.Status = CampusCardStatus.Normalize(status);
        card.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<CampusCardDto>.Ok(await ToDtoAsync(card, ct));
    }

    /// <summary>The full ledger for one card - the counter's transaction view.</summary>
    public async Task<MarketplaceResult<List<CampusCardTransactionDto>>> LedgerAsync(
        int staffId, int cardId, CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(staffId, ct);
        if (actor is null)
            return MarketplaceResult<List<CampusCardTransactionDto>>.Forbidden(StaffOnly);

        var rows = await _db.MarketplaceCardTransactions.AsNoTracking()
            .Where(t => t.CardId == cardId)
            .OrderByDescending(t => t.TransactionId)
            .Take(StaffTransactionLimit)
            .ToListAsync(ct);

        if (rows.Count == 0)
            return MarketplaceResult<List<CampusCardTransactionDto>>.NotFound("That card has no movements yet.");

        var orders = await OrdersByIdAsync(rows.Select(t => t.ReferenceOrderId).ToList(), ct);
        var performers = await UsersByIdAsync(rows.Where(t => t.PerformedByUserId.HasValue)
            .Select(t => t.PerformedByUserId!.Value).ToList(), ct);

        return MarketplaceResult<List<CampusCardTransactionDto>>.Ok(
            rows.Select(t => ToTransactionDto(t, orders.GetValueOrDefault(t.ReferenceOrderId ?? 0), performers)).ToList());
    }

    // =====================================================
    // INTERNAL
    // =====================================================

    /// <summary>
    /// Creates the card for a user if they do not have one yet. The number is
    /// derived from the user id, so it is stable and collision-free without a
    /// retry loop.
    /// </summary>
    private async Task<MarketplaceCampusCard> IssueAsync(int userId, string idNumber, CancellationToken ct)
    {
        var card = new MarketplaceCampusCard
        {
            UserId = userId,
            // Stable per user. The UNIQUE index on CardNumber is the real guard
            // if two issuances ever race.
            CardNumber = $"CDM-{userId:D6}",
            BalanceCentavos = 0,
            Status = CampusCardStatus.Active,
            IssuedAt = DateTime.UtcNow
        };

        _db.MarketplaceCampusCards.Add(card);
        await _db.SaveChangesAsync(ct);
        return card;
    }

    private async Task<CampusCardDto> ToDtoAsync(MarketplaceCampusCard card, CancellationToken ct)
    {
        var recent = await _db.MarketplaceCardTransactions.AsNoTracking()
            .Where(t => t.CardId == card.CardId)
            .OrderByDescending(t => t.TransactionId)
            .Take(RecentTransactionLimit)
            .ToListAsync(ct);

        var orders = await OrdersByIdAsync(recent.Select(t => t.ReferenceOrderId).ToList(), ct);
        var performers = await UsersByIdAsync(recent.Where(t => t.PerformedByUserId.HasValue)
            .Select(t => t.PerformedByUserId!.Value).ToList(), ct);

        return new CampusCardDto
        {
            CardId = card.CardId,
            UserId = card.UserId,
            FullName = card.User?.FullName ?? "",
            IdNumber = card.User?.IdNumber ?? "",
            AccountType = card.User?.Role ?? "",
            CardNumber = card.CardNumber,
            BalanceCentavos = card.BalanceCentavos,
            Status = card.Status,
            IssuedAt = card.IssuedAt,
            LastLoadedAt = card.LastLoadedAt,
            RecentTransactions = recent
                .Select(t => ToTransactionDto(t, orders.GetValueOrDefault(t.ReferenceOrderId ?? 0), performers))
                .ToList()
        };
    }

    /// <summary>
    /// Maps one ledger row, signing the amount so the UI never has to re-derive
    /// the direction from the type.
    /// </summary>
    private static CampusCardTransactionDto ToTransactionDto(
        MarketplaceCardTransaction t,
        MarketplaceOrder? order,
        IReadOnlyDictionary<int, User> performers) => new()
        {
            TransactionId = t.TransactionId,
            Type = t.Type,
            TypeLabel = TransactionLabel(t.Type),
            AmountCentavos = t.AmountCentavos,
            SignedAmountCentavos = t.Type == CampusCardTransactionType.Debit
                ? -t.AmountCentavos
                : t.AmountCentavos,
            BalanceAfterCentavos = t.BalanceAfterCentavos,
            ReferenceOrderId = t.ReferenceOrderId,
            OrderReference = order?.OrderReference ?? "",
            PerformedByName = t.PerformedByUserId.HasValue
                ? performers.GetValueOrDefault(t.PerformedByUserId.Value)?.FullName ?? ""
                : "",
            OperatorName = t.OperatorName ?? "",
            Notes = t.Notes ?? "",
            CreatedAt = t.CreatedAt
        };

    private static string TransactionLabel(string? type) => type switch
    {
        CampusCardTransactionType.Credit => "Loaded",
        CampusCardTransactionType.Debit => "Purchase",
        CampusCardTransactionType.Refund => "Refund",
        _ => "Movement"
    };

    private async Task<Dictionary<int, MarketplaceOrder>> OrdersByIdAsync(List<int?> ids, CancellationToken ct)
    {
        var wanted = ids.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (wanted.Count == 0) return new();

        return await _db.MarketplaceOrders.AsNoTracking()
            .Where(o => wanted.Contains(o.OrderId))
            .ToDictionaryAsync(o => o.OrderId, ct);
    }

    private async Task<Dictionary<int, User>> UsersByIdAsync(List<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new();

        return await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);
    }

    private const string StaffOnly = "Marketplace Staff access required.";
}
