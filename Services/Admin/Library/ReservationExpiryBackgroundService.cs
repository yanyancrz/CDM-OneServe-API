using CDM_OneServe_API.Interfaces.Admin.Library;

namespace CDM_OneServe_API.Services.Admin.Library;

/// <summary>
/// Runs once at startup, then every 5 minutes:
/// active reservations past their ExpirationDate become "Expired",
/// and a copy held for a ReadyForPickup reservation goes back on the shelf.
/// </summary>
public class ReservationExpiryBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReservationExpiryBackgroundService> _logger;

    public ReservationExpiryBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ReservationExpiryBackgroundService> logger)
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
            // The app is shutting down.
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            // AppDbContext is scoped, so every run gets its own scope.
            await using var scope = _scopeFactory.CreateAsyncScope();

            var reservations = scope.ServiceProvider.GetRequiredService<IAdminReservationService>();

            var count = await reservations.ExpireOverdueAsync(stoppingToken);

            if (count > 0)
                _logger.LogInformation("Reservation expiry: {Count} reservation(s) marked Expired.", count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never crash the API because of this job. It tries again on the next tick.
            _logger.LogError(ex, "Reservation expiry job failed.");
        }
    }
}
