namespace CDM_OneServe_API.Services.Guidance;

/// <summary>
/// Runs at startup, then every 5 minutes, and does the two lifecycle jobs
/// that must never depend on a person clicking a button:
///   1. unanswered Pending appointment requests whose date has passed become
///      "Expired" (port of utils/expireStaleAppointments.js);
///   2. Confirmed sessions nobody attended within the grace period are
///      terminated as "NoShow" automatically (see
///      <see cref="GuidanceCounselorService.AutoTerminateNoShowsAsync"/>).
///
/// The interval used to be hourly, which made an automatic no-show land up
/// to an hour late; 5 minutes is still cheap (two indexed queries) and keeps
/// the "terminated even without the counselor" behaviour timely.
/// </summary>
public class GuidanceExpiryBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

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
            var counselors = scope.ServiceProvider.GetRequiredService<GuidanceCounselorService>();

            var expired = await students.ExpireStaleAsync(ct);
            if (expired > 0)
                _logger.LogInformation("Guidance expiry: {Count} unanswered request(s) marked Expired.", expired);

            var noShows = await counselors.AutoTerminateNoShowsAsync(ct);
            if (noShows > 0)
                _logger.LogInformation("Guidance no-show: {Count} unattended confirmed session(s) automatically terminated.", noShows);
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
