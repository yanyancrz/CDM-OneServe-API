using CDM_OneServe_API.Data;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Expires orders that have been sitting at the counter past their collect-by
/// deadline, and gives their stock back.
///
/// Created by Database/Marketplace/010_order_lifecycle.sql.
///
/// WHY THIS JOB EXISTS
///   Before it, a buyer could place a cash order and never collect it, and the
///   hold on marketplace_inventory.ReservedQuantity was never returned. There was
///   no code path at all that touched an uncollected order, and
///   MarketplaceOrderStatus.CanCancel forbids cancelling once an order reaches
///   ReadyForPickup - so even a diligent member of staff could not free the units.
///   The leaked hold is self-reinforcing: it trips the guard on
///   MarketplaceInventoryService.SaveAsync ("N unit(s) are reserved by open
///   orders. Cancel or complete those orders first."), so it then blocks the stock
///   from being managed at all.
///
/// WHY EVERY 5 MINUTES
///   The deadline is 24 hours, so there is no value in running more often - the
///   job is cheap (one index seek on IX_Order_Expiry) and the interval only sets
///   how stale the buyer's view is. Five minutes matches the other short-interval
///   jobs in this API and keeps the "expired" state from surprising anyone.
///
/// WHY IT CANNOT CRASH THE API
///   The whole loop is wrapped so a failure is logged and swallowed, exactly like
///   PushMaintenanceHostedService and ReservationExpiryBackgroundService. A
///   failing expiry job must never take the API down; it simply retries next tick.
///   Losing a tick is safe because expiry is idempotent - the order's
///   StockSettled flag is claimed before anything is released, so a second run
///   over the same deadline is a no-op.
///
/// WHY IT RESOLVES A SCOPED SERVICE
///   BackgroundService is a singleton, and AppDbContext is scoped. Creating a
///   scope per tick is the only correct way to use it - injecting the context
///   directly would give every tick the same disposed instance.
/// </summary>
public class MarketplaceOrderExpiryHostedService : BackgroundService
{
    /// <summary>How often the deadline is checked.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MarketplaceOrderExpiryHostedService> _logger;

    public MarketplaceOrderExpiryHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<MarketplaceOrderExpiryHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        var now = DateTime.UtcNow;

        await using var scope = _scopeFactory.CreateAsyncScope();

        var orders = scope.ServiceProvider.GetRequiredService<MarketplaceOrderService>();

        try
        {
            var expired = await orders.ExpireOverdueOrdersAsync(now, ct: stoppingToken);

            if (expired > 0)
            {
                _logger.LogInformation(
                    "Marketplace order expiry: {Expired} uncollected order(s) expired and their stock released.",
                    expired);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A failing expiry job must never take the API down. It retries next
            // tick, and expiry is idempotent, so a missed run costs nothing but
            // staleness.
            _logger.LogError(ex, "Marketplace order expiry job failed.");
        }
    }
}
