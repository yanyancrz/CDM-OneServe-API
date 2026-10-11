using CDM_OneServe_API.Data;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Guidance;

/// <summary>
/// Runs every 15 minutes: Confirmed sessions starting within the hour get one
/// "please arrive on time" reminder (in-app row + browser push + realtime, via
/// GuidanceNotificationService). Sessions already reminded are skipped by
/// looking for the earlier reminder row, so a restart can never double-send.
/// </summary>
public class GuidanceReminderBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private const string ReminderType = "GUIDANCE_APPOINTMENT_REMINDER";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GuidanceReminderBackgroundService> _logger;

    public GuidanceReminderBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<GuidanceReminderBackgroundService> logger)
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
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notifications = scope.ServiceProvider.GetRequiredService<GuidanceNotificationService>();

            var now = GuidanceClock.Now;
            var today = GuidanceClock.Today;

            // Sessions today that start within the next hour (and haven't started).
            var upcoming = await db.GuidanceAppointments.AsNoTracking()
                .Where(a => a.Status == "Confirmed" && a.Date == today)
                .Select(a => new { a.Id, a.StudentId, a.TimeSlot })
                .ToListAsync(ct);

            var sent = 0;

            foreach (var a in upcoming)
            {
                var startMinutes = GuidanceClock.ParseSlot(a.TimeSlot);
                if (startMinutes == null) continue;

                var startsIn = startMinutes.Value - (now.Hour * 60 + now.Minute);
                if (startsIn <= 0 || startsIn > 60) continue;

                var refTag = $"[#{a.Id}]";
                var alreadySent = await db.Notifications.AsNoTracking().AnyAsync(
                    n => n.UserId == a.StudentId && n.Type == ReminderType && n.Message.Contains(refTag), ct);
                if (alreadySent) continue;

                await notifications.CreateAsync(
                    a.StudentId,
                    ReminderType,
                    "Your session starts in 1 hour",
                    $"Please arrive on time - your guidance session is at {a.TimeSlot} today {refTag}.",
                    ct);
                sent++;
            }

            if (sent > 0)
                _logger.LogInformation("Guidance reminder: {Count} arrive-on-time reminder(s) sent.", sent);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never let one bad run kill the loop (e.g. tables not created yet).
            _logger.LogWarning(ex, "Guidance reminder run failed.");
        }
    }
}
