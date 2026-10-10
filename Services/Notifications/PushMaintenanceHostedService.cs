using CDM_OneServe_API.Data;
using CDM_OneServe_API.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Notifications;

/// <summary>
/// Housekeeping for the Web Push tables, and the safety net for the
/// Node notification service.
///
/// If the Node process crashes mid-delivery it leaves rows in
/// "Processing"; without this job those notifications would be lost
/// forever. Runs once a day - push delivery itself is not its job.
/// </summary>
public class PushMaintenanceHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>A row stuck in Processing longer than this was abandoned.</summary>
    private static readonly TimeSpan StuckProcessing = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan SentRetention = TimeSpan.FromDays(30);
    private static readonly TimeSpan FailedRetention = TimeSpan.FromDays(7);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PushMaintenanceHostedService> _logger;

    public PushMaintenanceHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<PushMaintenanceHostedService> logger)
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
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            // 1. Give abandoned deliveries back to the worker.
            var requeued = await db.PushOutbox
                .Where(m => m.Status == "Processing" && m.ScheduledAt < now - StuckProcessing)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.Status, "Pending")
                    .SetProperty(m => m.ScheduledAt, now), stoppingToken);

            // 2. Retention: the outbox is a queue, not an archive.
            var deletedSent = await db.PushOutbox
                .Where(m => m.Status == "Sent" && (m.ProcessedAt ?? m.CreatedAt) < now - SentRetention)
                .ExecuteDeleteAsync(stoppingToken);

            var deletedFailed = await db.PushOutbox
                .Where(m => (m.Status == "Failed" || m.Status == "Cancelled") &&
                            (m.ProcessedAt ?? m.CreatedAt) < now - FailedRetention)
                .ExecuteDeleteAsync(stoppingToken);

            var deletedLogs = await db.PushDeliveryLogs
                .Where(l => l.CreatedAt < now - SentRetention)
                .ExecuteDeleteAsync(stoppingToken);

            // 3. Browsers the push services have forgotten for 90 days.
            var deadSubscriptions = await db.PushSubscriptions
                .Where(s => !s.IsActive &&
                            (s.DeactivatedAt == null || s.DeactivatedAt < now - PushSubscriptionService.ExpiredRetention))
                .ExecuteDeleteAsync(stoppingToken);

            _logger.LogInformation(
                "Web Push maintenance: {Requeued} requeued, {Sent} sent-pruned, {Failed} failed-pruned, " +
                "{Logs} log-pruned, {Subscriptions} dead-subscriptions-pruned.",
                requeued, deletedSent, deletedFailed, deletedLogs, deadSubscriptions);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Web Push maintenance job failed.");
        }
    }
}
