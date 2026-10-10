namespace CDM_OneServe_API.Services.Guidance;

/// <summary>
/// Runs at startup, then hourly: unanswered Pending appointment requests whose
/// date has passed become "Expired" (port of utils/expireStaleAppointments.js).
/// </summary>
public class GuidanceExpiryBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GuidanceExpiryBackgroundService> _logger;

    public GuidanceExpiryBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<GuidanceExpiryBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var students = scope.ServiceProvider.GetRequiredService<GuidanceStudentService>();

            var count = await students.ExpireStaleAsync(ct);
            if (count > 0)
                _logger.LogInformation("Guidance expiry: {Count} unanswered request(s) marked Expired.", count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never let one bad run kill the loop (e.g. tables not created yet).
            _logger.LogWarning(ex, "Guidance expiry run failed.");
        }
    }
}