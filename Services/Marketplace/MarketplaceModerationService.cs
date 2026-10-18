using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Marketplace;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Moderation: blocks, reports, support tickets, and bans.
///
/// Created by Database/Marketplace/015_moderation.sql.
///
/// THREE THINGS, NOT ONE
///   Blocks are a USER action: "I do not want to see this person or store."
///   Reports are a USER action: "I am flagging this for an administrator."
///   Bans are an ADMIN action: "this account or store is not allowed here."
///   They are deliberately independent: a report never bans anything by itself,
///   and a block never removes anything. Only an administrator's decision does.
///
/// MUTUAL HIDING
///   A store is hidden from a buyer when:
///     (a) the buyer blocked that store,
///     (b) the buyer blocked the store's owner, or
///     (c) the owner blocked the buyer.
///   The same rule hides a user's public profile. A block is therefore
///   symmetrical for visibility even though only one side creates it.
///
/// BANS AND OPEN ORDERS
///   Banning an account or a store suspends every open order the buyer placed,
///   every open order at that store, and every open order at any store the
///   banned user owns:
///     unpaid  -> released and cancelled;
///     paid    -> return_requested + refund_pending, with an auto-approved
///                refund request (reason: "Order suspended by moderation").
///   Completed purchases stay in history. See 010_order_lifecycle.sql for the
///   stock-settle lock that makes this safe against concurrent cancellation.
/// </summary>
public class MarketplaceModerationService
{
    private readonly AppDbContext _db;
    private readonly MarketplaceOrderService _orders;
    private readonly MarketplaceInventoryService _inventory;

    public MarketplaceModerationService(
        AppDbContext db,
        MarketplaceOrderService orders,
        MarketplaceInventoryService inventory)
    {
        _db = db;
        _orders = orders;
        _inventory = inventory;
    }

    // =====================================================
    // BLOCKS - user action
    // =====================================================

    /// <summary>
    /// Blocks a user or a store. Exactly one target. Cannot block yourself, and
    /// cannot block a store you own.
    /// </summary>
    public async Task<MarketplaceResult<bool>> BlockAsync(
        int userId, int? targetUserId, int? targetStoreId, CancellationToken ct = default)
    {
        if (targetUserId is null == (targetStoreId is null))
            return MarketplaceResult<bool>.BadRequest("Block either a user or a store, not both and not neither.");

        if (targetUserId == userId)
            return MarketplaceResult<bool>.BadRequest("You cannot block yourself.");

        if (targetStoreId is int storeId)
        {
            var store = await _db.MarketplaceStores.AsNoTracking()
                .FirstOrDefaultAsync(s => s.StoreId == storeId, ct);

            if (store is null)
                return MarketplaceResult<bool>.NotFound("That store does not exist.");

            if (store.OwnerUserId == userId)
                return MarketplaceResult<bool>.BadRequest("You cannot block your own store.");
        }

        var exists = await _db.MarketplaceBlocks.AsNoTracking()
            .AnyAsync(b => b.UserId == userId
                           && b.TargetUserId == targetUserId
                           && b.TargetStoreId == targetStoreId, ct);

        if (exists)
            return MarketplaceResult<bool>.Conflict("You already blocked that.");

        _db.MarketplaceBlocks.Add(new MarketplaceBlock
        {
            UserId = userId,
            TargetUserId = targetUserId,
            TargetStoreId = targetStoreId,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        return MarketplaceResult<bool>.Ok(true, "Blocked.");
    }

    /// <summary>Removes a block the caller created.</summary>
    public async Task<MarketplaceResult<bool>> UnblockAsync(
        int userId, int blockId, CancellationToken ct = default)
    {
        var block = await _db.MarketplaceBlocks
            .FirstOrDefaultAsync(b => b.BlockId == blockId && b.UserId == userId, ct);

        if (block is null)
            return MarketplaceResult<bool>.NotFound("That block does not exist.");

        _db.MarketplaceBlocks.Remove(block);
        await _db.SaveChangesAsync(ct);
        return MarketplaceResult<bool>.Ok(true, "Unblocked.");
    }

    /// <summary>
    /// True when a store should be hidden from this buyer. Mutual and
    /// transitive: the buyer blocked the store, the buyer blocked the owner, or
    /// the owner blocked the buyer.
    /// </summary>
    public async Task<bool> StoreHiddenAsync(int userId, int storeId, CancellationToken ct = default)
    {
        var store = await _db.MarketplaceStores.AsNoTracking()
            .FirstOrDefaultAsync(s => s.StoreId == storeId, ct);

        if (store is null) return true;

        // The buyer blocked the store itself.
        var blockedStore = await _db.MarketplaceBlocks.AsNoTracking()
            .AnyAsync(b => b.UserId == userId && b.TargetStoreId == storeId, ct);

        if (blockedStore) return true;

        // The buyer blocked the owner, or the owner blocked the buyer.
        if (store.OwnerUserId is int ownerId)
        {
            var buyerBlockedOwner = await _db.MarketplaceBlocks.AsNoTracking()
                .AnyAsync(b => b.UserId == userId && b.TargetUserId == ownerId, ct);

            if (buyerBlockedOwner) return true;

            var ownerBlockedBuyer = await _db.MarketplaceBlocks.AsNoTracking()
                .AnyAsync(b => b.UserId == ownerId && b.TargetUserId == userId, ct);

            if (ownerBlockedBuyer) return true;
        }

        return false;
    }

    // =====================================================
    // REPORTS - user action, admin resolution
    // =====================================================

    /// <summary>
    /// Files a report. Exactly one target; reason 5-2000 characters. A report
    /// has no automatic effect - it only creates a queue entry for an admin.
    /// </summary>
    public async Task<MarketplaceResult<bool>> ReportAsync(
        int userId, int? targetUserId, int? targetStoreId, string reason, CancellationToken ct = default)
    {
        if (targetUserId is null == (targetStoreId is null))
            return MarketplaceResult<bool>.BadRequest("Report either a user or a store, not both and not neither.");

        var text = (reason ?? "").Trim();
        if (text.Length is < 5 or > 2000)
            return MarketplaceResult<bool>.BadRequest("Say what happened (5-2000 characters).");

        if (targetUserId == userId)
            return MarketplaceResult<bool>.BadRequest("You cannot report yourself.");

        if (targetStoreId is int storeId)
        {
            var store = await _db.MarketplaceStores.AsNoTracking()
                .FirstOrDefaultAsync(s => s.StoreId == storeId, ct);

            if (store is null)
                return MarketplaceResult<bool>.NotFound("That store does not exist.");
        }

        _db.MarketplaceReports.Add(new MarketplaceReport
        {
            ReporterId = userId,
            TargetUserId = targetUserId,
            TargetStoreId = targetStoreId,
            Reason = text,
            Status = "open",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        return MarketplaceResult<bool>.Ok(true, "Report filed. An administrator will review it.");
    }

    /// <summary>Reports waiting for or already reviewed by an administrator.</summary>
    public async Task<MarketplaceResult<List<MarketplaceReportDto>>> ListReportsAsync(
        int adminId, string? status, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(adminId, ct))
            return MarketplaceResult<List<MarketplaceReportDto>>.Forbidden(AdminOnly);

        var query = _db.MarketplaceReports.AsNoTracking()
            .Include(r => r.Reporter)
            .Include(r => r.TargetUser)
            .Include(r => r.TargetStore)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = status.Trim().ToLowerInvariant();
            query = query.Where(r => r.Status == wanted);
        }

        var rows = await query
            .OrderBy(r => r.Status == "open" ? 0 : 1)
            .ThenByDescending(r => r.CreatedAt)
            .Take(300)
            .ToListAsync(ct);

        return MarketplaceResult<List<MarketplaceReportDto>>.Ok(rows.Select(ToReportDto).ToList());
    }

    /// <summary>
    /// Administrator's decision on a report. reviewed or dismissed; the report
    /// itself is never deleted, so the audit trail stays complete.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceReportDto>> ResolveReportAsync(
        int adminId, int reportId, string status, string? resolution, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(adminId, ct))
            return MarketplaceResult<MarketplaceReportDto>.Forbidden(AdminOnly);

        if (status is not ("reviewed" or "dismissed"))
            return MarketplaceResult<MarketplaceReportDto>.BadRequest("Status must be reviewed or dismissed.");

        var report = await _db.MarketplaceReports
            .Include(r => r.Reporter)
            .Include(r => r.TargetUser)
            .Include(r => r.TargetStore)
            .FirstOrDefaultAsync(r => r.ReportId == reportId, ct);

        if (report is null)
            return MarketplaceResult<MarketplaceReportDto>.NotFound("That report does not exist.");

        report.Status = status;
        report.Resolution = string.IsNullOrWhiteSpace(resolution) ? null : resolution.Trim();
        report.ReviewedBy = adminId;
        report.ReviewedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return MarketplaceResult<MarketplaceReportDto>.Ok(ToReportDto(report), $"Report {status}.");
    }

    // =====================================================
    // SUPPORT TICKETS
    // =====================================================

    /// <summary>Opens a support ticket. Any signed-in account.</summary>
    public async Task<MarketplaceResult<MarketplaceSupportTicketDto>> OpenTicketAsync(
        int userId, string subject, string body, CancellationToken ct = default)
    {
        var s = (subject ?? "").Trim();
        var b = (body ?? "").Trim();

        if (s.Length is < 3 or > 150)
            return MarketplaceResult<MarketplaceSupportTicketDto>.BadRequest("Subject must be 3-150 characters.");

        if (b.Length is < 5 or > 3000)
            return MarketplaceResult<MarketplaceSupportTicketDto>.BadRequest("Message must be 5-3000 characters.");

        var ticket = new MarketplaceSupportTicket
        {
            UserId = userId,
            Subject = s,
            Body = b,
            Status = "open",
            CreatedAt = DateTime.UtcNow
        };

        _db.MarketplaceSupportTickets.Add(ticket);
        await _db.SaveChangesAsync(ct);

        var saved = await _db.MarketplaceSupportTickets.AsNoTracking()
            .Include(t => t.User)
            .FirstAsync(t => t.TicketId == ticket.TicketId, ct);

        return MarketplaceResult<MarketplaceSupportTicketDto>.Created(ToTicketDto(saved), "Ticket opened.");
    }

    /// <summary>The caller's own tickets.</summary>
    public async Task<MarketplaceResult<List<MarketplaceSupportTicketDto>>> MyTicketsAsync(
        int userId, CancellationToken ct = default)
    {
        var rows = await _db.MarketplaceSupportTickets.AsNoTracking()
            .Include(t => t.User)
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        return MarketplaceResult<List<MarketplaceSupportTicketDto>>.Ok(rows.Select(ToTicketDto).ToList());
    }

    /// <summary>All tickets. Administrator only.</summary>
    public async Task<MarketplaceResult<List<MarketplaceSupportTicketDto>>> ListTicketsAsync(
        int adminId, string? status, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(adminId, ct))
            return MarketplaceResult<List<MarketplaceSupportTicketDto>>.Forbidden(AdminOnly);

        var query = _db.MarketplaceSupportTickets.AsNoTracking()
            .Include(t => t.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var wanted = status.Trim().ToLowerInvariant();
            query = query.Where(t => t.Status == wanted);
        }

        var rows = await query
            .OrderBy(t => t.Status == "open" ? 0 : t.Status == "answered" ? 1 : 2)
            .ThenByDescending(t => t.CreatedAt)
            .Take(300)
            .ToListAsync(ct);

        return MarketplaceResult<List<MarketplaceSupportTicketDto>>.Ok(rows.Select(ToTicketDto).ToList());
    }

    /// <summary>
    /// Administrator replies to a ticket. The reply is overwritten rather than
    /// appended, because a ticket has one answer, not a thread.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceSupportTicketDto>> RespondAsync(
        int adminId, int ticketId, string response, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(adminId, ct))
            return MarketplaceResult<MarketplaceSupportTicketDto>.Forbidden(AdminOnly);

        var text = (response ?? "").Trim();
        if (text.Length is < 1 or > 3000)
            return MarketplaceResult<MarketplaceSupportTicketDto>.BadRequest("Reply must be 1-3000 characters.");

        var ticket = await _db.MarketplaceSupportTickets
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TicketId == ticketId, ct);

        if (ticket is null)
            return MarketplaceResult<MarketplaceSupportTicketDto>.NotFound("That ticket does not exist.");

        ticket.Response = text;
        ticket.Status = "answered";
        ticket.RespondedBy = adminId;
        ticket.RespondedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return MarketplaceResult<MarketplaceSupportTicketDto>.Ok(ToTicketDto(ticket), "Reply sent.");
    }

    /// <summary>Closes a ticket. Administrator only.</summary>
    public async Task<MarketplaceResult<MarketplaceSupportTicketDto>> CloseAsync(
        int adminId, int ticketId, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(adminId, ct))
            return MarketplaceResult<MarketplaceSupportTicketDto>.Forbidden(AdminOnly);

        var ticket = await _db.MarketplaceSupportTickets
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TicketId == ticketId, ct);

        if (ticket is null)
            return MarketplaceResult<MarketplaceSupportTicketDto>.NotFound("That ticket does not exist.");

        ticket.Status = "closed";
        ticket.ClosedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return MarketplaceResult<MarketplaceSupportTicketDto>.Ok(ToTicketDto(ticket), "Ticket closed.");
    }

    // =====================================================
    // BANS - administrator action
    // =====================================================

    /// <summary>
    /// Bans or unbans an account. Administrator only.
    ///
    /// Cannot ban yourself, a superadmin, or an admin (unless you are a
    /// superadmin). Banning suspends every open order the buyer placed and every
    /// open order at any store they own.
    /// </summary>
    public async Task<MarketplaceResult<bool>> SetAccountBanAsync(
        int adminId, int targetUserId, bool banned, string? reason, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(adminId, ct))
            return MarketplaceResult<bool>.Forbidden(AdminOnly);

        if (adminId == targetUserId)
            return MarketplaceResult<bool>.BadRequest("You cannot ban yourself.");

        var target = await _db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, ct);

        if (target is null)
            return MarketplaceResult<bool>.NotFound("That account does not exist.");

        if (target.Role is "SuperAdmin" || (target.Role is "Admin" && !await IsSuperAdminAsync(adminId, ct)))
            return MarketplaceResult<bool>.Forbidden("You cannot ban an administrator.");

        var newStatus = banned ? "Banned" : "Active";

        if (target.AccountStatus == newStatus)
            return MarketplaceResult<bool>.Ok(true, $"That account is already {newStatus.ToLowerInvariant()}.");

        target.AccountStatus = newStatus;
        await _db.SaveChangesAsync(ct);

        if (banned)
        {
            await SuspendOpenOrdersForUserAsync(targetUserId, reason, ct);
        }

        return MarketplaceResult<bool>.Ok(true, banned ? "Account banned." : "Account unbanned.");
    }

    /// <summary>
    /// Bans or unbans a store. Administrator only.
    ///
    /// Unbanning lands on `paused`, never `active`: reopening is deliberately
    /// the owner's decision, not the administrator's. Banning suspends every
    /// open order at that store.
    /// </summary>
    public async Task<MarketplaceResult<bool>> SetStoreBanAsync(
        int adminId, int storeId, bool banned, CancellationToken ct = default)
    {
        if (!await IsAdminAsync(adminId, ct))
            return MarketplaceResult<bool>.Forbidden(AdminOnly);

        var store = await _db.MarketplaceStores.FirstOrDefaultAsync(s => s.StoreId == storeId, ct);

        if (store is null)
            return MarketplaceResult<bool>.NotFound("That store does not exist.");

        if (banned)
        {
            if (store.Status == MarketplaceStoreStatus.Banned)
                return MarketplaceResult<bool>.Ok(true, "That store is already banned.");

            store.Status = MarketplaceStoreStatus.Banned;
            await _db.SaveChangesAsync(ct);

            await SuspendOpenOrdersForStoreAsync(storeId, ct);
        }
        else
        {
            if (store.Status != MarketplaceStoreStatus.Banned)
                return MarketplaceResult<bool>.BadRequest("That store is not banned.");

            store.Status = MarketplaceStoreStatus.Paused;
            await _db.SaveChangesAsync(ct);
        }

        return MarketplaceResult<bool>.Ok(true, banned ? "Store banned." : "Store unbanned (paused).");
    }

    // =====================================================
    // INTERNAL - suspend open orders
    // =====================================================

    /// <summary>
    /// Suspends every open order a buyer placed, every open order at a store,
    /// and every open order at any store the user owns.
    ///
    ///   unpaid  -> released and cancelled;
    ///   paid    -> return_requested + refund_pending, with an auto-approved
    ///              refund request (reason: "Order suspended by moderation").
    ///
    /// Completed purchases stay in history. Stock is released through the
    /// StockSettled lock, so concurrent cancellation or expiry cannot double-
    /// release.
    /// </summary>
    private async Task SuspendOpenOrdersForUserAsync(int userId, string? reason, CancellationToken ct)
    {
        var storeIds = await _db.MarketplaceStores.AsNoTracking()
            .Where(s => s.OwnerUserId == userId)
            .Select(s => s.StoreId)
            .ToListAsync(ct);

        var workspaceIds = await _db.MarketplaceStores.AsNoTracking()
            .Where(s => s.OwnerUserId == userId && s.WorkspaceId != null)
            .Select(s => s.WorkspaceId!.Value)
            .ToListAsync(ct);

        var orders = await _db.MarketplaceOrders
            .Include(o => o.Items)
            .Where(o => o.Status != MarketplaceOrderStatus.Completed
                        && o.Status != MarketplaceOrderStatus.Cancelled
                        && o.Status != MarketplaceOrderStatus.Expired
                        && o.Status != MarketplaceOrderStatus.Refunded
                        && (o.UserId == userId
                            || (o.WorkspaceId != null && workspaceIds.Contains(o.WorkspaceId.Value))))
            .ToListAsync(ct);

        await SuspendOrdersAsync(orders, reason, ct);
    }

    private async Task SuspendOpenOrdersForStoreAsync(int storeId, CancellationToken ct)
    {
        var store = await _db.MarketplaceStores.AsNoTracking()
            .FirstOrDefaultAsync(s => s.StoreId == storeId, ct);

        if (store?.WorkspaceId is not int workspaceId) return;

        var orders = await _db.MarketplaceOrders
            .Include(o => o.Items)
            .Where(o => o.Status != MarketplaceOrderStatus.Completed
                        && o.Status != MarketplaceOrderStatus.Cancelled
                        && o.Status != MarketplaceOrderStatus.Expired
                        && o.Status != MarketplaceOrderStatus.Refunded
                        && o.WorkspaceId == workspaceId)
            .ToListAsync(ct);

        await SuspendOrdersAsync(orders, "Store banned", ct);
    }

    private async Task SuspendOrdersAsync(
        List<MarketplaceOrder> orders, string? reason, CancellationToken ct)
    {
        foreach (var order in orders)
        {
            if (order.PaymentStatus == "unpaid")
            {
                // Unpaid: release and cancel.
                if (await _inventory.ReleaseOrderStockOnceAsync(
                    order, 0, null, null, reason ?? "Order suspended", "Suspended", ct))
                {
                    order.StockSettled = true;
                    order.Status = MarketplaceOrderStatus.Cancelled;
                    order.CancelledAt = DateTime.UtcNow;
                    order.ExpiryReason = reason ?? "Order suspended by moderation";
                    order.UpdatedAt = DateTime.UtcNow;
                }
            }
            else if (order.PaymentStatus == "paid")
            {
                // Paid: convert to return_requested + auto-approved refund.
                order.Status = MarketplaceOrderStatus.ReturnRequested;
                order.PaymentStatus = "refund_pending";
                order.UpdatedAt = DateTime.UtcNow;

                var exists = await _db.MarketplaceRefundRequests.AsNoTracking()
                    .AnyAsync(r => r.OrderId == order.OrderId, ct);

                if (!exists)
                {
                    _db.MarketplaceRefundRequests.Add(new MarketplaceRefundRequest
                    {
                        OrderId = order.OrderId,
                        Reason = reason ?? "Order suspended by moderation",
                        Status = MarketplaceRefundStatus.Approved,
                        Restock = true,
                        CreatedAt = DateTime.UtcNow,
                        ReviewedAt = DateTime.UtcNow
                    });
                }
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    // =====================================================
    // DTO MAPPING
    // =====================================================

    private static MarketplaceReportDto ToReportDto(MarketplaceReport r) => new()
    {
        ReportId = r.ReportId,
        ReporterId = r.ReporterId,
        ReporterName = r.Reporter?.FullName ?? "",
        TargetUserId = r.TargetUserId,
        TargetUserName = r.TargetUser?.FullName ?? "",
        TargetStoreId = r.TargetStoreId,
        TargetStoreName = r.TargetStore?.Name ?? "",
        Reason = r.Reason,
        Status = r.Status,
        Resolution = r.Resolution,
        CreatedAt = r.CreatedAt,
        ReviewedAt = r.ReviewedAt
    };

    private static MarketplaceSupportTicketDto ToTicketDto(MarketplaceSupportTicket t) => new()
    {
        TicketId = t.TicketId,
        UserId = t.UserId,
        FullName = t.User?.FullName ?? "",
        Email = t.User?.Email ?? "",
        Subject = t.Subject,
        Body = t.Body,
        Status = t.Status,
        Response = t.Response,
        CreatedAt = t.CreatedAt,
        RespondedAt = t.RespondedAt,
        ClosedAt = t.ClosedAt
    };

    private async Task<bool> IsAdminAsync(int userId, CancellationToken ct) =>
        await _db.Users.AsNoTracking().AnyAsync(
            u => u.Id == userId
                 && u.AccountStatus == "Active"
                 && (u.Role == "Admin" || u.Role == "SuperAdmin"
                     || (u.Role == "MarketplaceAdmin" && u.AdminModule == "Marketplace")), ct);

    private async Task<bool> IsSuperAdminAsync(int userId, CancellationToken ct) =>
        await _db.Users.AsNoTracking().AnyAsync(
            u => u.Id == userId
                 && u.AccountStatus == "Active"
                 && u.Role == "SuperAdmin", ct);

    private const string AdminOnly = "Administrator access required.";
}
