using CDM_OneServe_API.Data;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Sends queued marketplace emails (receipts and refunds) with retries.
///
/// Created by Database/Marketplace/017_email_outbox.sql.
///
/// Runs every minute. Claims up to 10 due jobs, sends each, and finishes it.
/// A failed send is rescheduled with exponential backoff (2, 4, 8, ... 128
/// minutes). After 8 attempts the job is abandoned so a permanently broken
/// recipient does not block the queue forever.
///
/// WHY THIS JOB EXISTS
///   Sending an email inside the request that created the order means a
///   transient SMTP failure loses the receipt forever, and a slow provider
///   makes the buyer wait. The outbox decouples the two: the order is
///   committed, a row is queued, and a worker sends it with retries.
///
/// WHY IT CANNOT CRASH THE API
///   The whole loop is wrapped so a failure is logged and swallowed. A failing
///   email job must never take the API down; it simply retries next tick.
/// </summary>
public class MarketplaceEmailOutboxHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MarketplaceEmailOutboxHostedService> _logger;

    public MarketplaceEmailOutboxHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<MarketplaceEmailOutboxHostedService> logger)
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
        await using var scope = _scopeFactory.CreateAsyncScope();

        var outbox = scope.ServiceProvider.GetRequiredService<MarketplaceEmailOutboxService>();

        try
        {
            var claimed = await outbox.ClaimAsync(10, stoppingToken);

            if (claimed.Count == 0) return;

            var sent = 0;
            var failed = 0;

            foreach (var job in claimed)
            {
                if (await outbox.SendAsync(job, stoppingToken))
                    sent++;
                else
                    failed++;
            }

            if (sent > 0 || failed > 0)
            {
                _logger.LogInformation(
                    "Marketplace email outbox: {Sent} sent, {Failed} failed.",
                    sent, failed);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Marketplace email outbox job failed.");
        }
    }
}
