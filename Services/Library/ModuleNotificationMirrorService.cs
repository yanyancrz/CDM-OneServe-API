using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CDM_OneServe_API.Services.Library;

/// <summary>
/// Mirrors MODULE notifications into the global notifications table
/// every 5 minutes, so the OneServe bell lists notices from every
/// module - "New from Library", "New from Lost &amp; Found", and so on.
///
/// Guidance already writes to the global table directly (GUIDANCE_*
/// types), so it needs no mirror. Clinic is a separate system, and
/// Marketplace has no notifications. Modules with their own table
/// are handled below, one block each.
///
/// Each mirrored row's Type is "{PREFIX}_{moduleNotificationId}" - the
/// bell classifies the leading word as the module, and the id suffix
/// dedupes the mirror.
///
/// Read state is synced both ways: reading a notice in the bell also
/// reads it in its module, and vice versa. Reading is a one-way
/// transition, so syncing either direction is safe.
/// </summary>
public class ModuleNotificationMirrorService : BackgroundService
{
    private const string LibraryPrefix = "LIBRARY_";

    private const string LostFoundPrefix = "LOSTFOUND_";

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    // The bell shows the 20 newest notices, so mirroring a month of
    // history is more than enough and keeps each run bounded.
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromDays(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ModuleNotificationMirrorService> _logger;

    public ModuleNotificationMirrorService(
        IServiceScopeFactory scopeFactory,
        ILogger<ModuleNotificationMirrorService> logger)
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

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var cutoff = DateTime.Now - HistoryWindow;
            var added = 0;

            // ---- LIBRARY ----

            var libraryNotices = await db.LibraryNotifications
                .Where(n => n.CreatedAt >= cutoff)
                .ToListAsync(stoppingToken);

            added += await InsertMissingAsync(
                db, LibraryPrefix, libraryNotices, stoppingToken);

            await PropagateReadsAsync(
                db, LibraryPrefix, libraryNotices, stoppingToken);

            // ---- LOST & FOUND ----

            var lostFoundNotices = await db.LostFoundNotifications
                .Where(n => n.CreatedAt >= cutoff)
                .ToListAsync(stoppingToken);

            added += await InsertMissingAsync(
                db, LostFoundPrefix, lostFoundNotices, stoppingToken);

            await PropagateReadsAsync(
                db, LostFoundPrefix, lostFoundNotices, stoppingToken);

            if (added > 0)
                _logger.LogInformation(
                    "Module notification mirror: {Added} notice(s) copied to the OneServe bell.",
                    added
                );
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never crash the API because of this job.
            _logger.LogError(ex, "Module notification mirror job failed.");
        }
    }

    /// <summary>
    /// Copies any unmiorrored module notices into the global table and
    /// marks global rows read whose module row was read after mirroring.
    /// </summary>
    private static async Task<int> InsertMissingAsync<TNotice>(
        AppDbContext db,
        string prefix,
        List<TNotice> notices,
        CancellationToken stoppingToken)
        where TNotice : class
    {
        if (notices.Count == 0)
            return 0;

        var mirrored = await db.Notifications
            .Where(n => n.Type.StartsWith(prefix))
            .ToListAsync(stoppingToken);

        var mirroredIds = new HashSet<int>();

        foreach (var global in mirrored)
        {
            var id = ParseId(prefix, global.Type);

            if (id is not null)
                mirroredIds.Add(id.Value);
        }

        var added = 0;
        var readChanged = false;

        foreach (var notice in notices)
        {
            var (id, userId, title, message, isRead, _) = ReadNotice(notice);

            if (mirroredIds.Contains(id))
                continue;

            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Type = $"{prefix}{id}",
                IsRead = isRead,
                CreatedAt = ReadNotice(notice).CreatedAt,
            });

            added++;
        }

        if (added > 0)
            await db.SaveChangesAsync(stoppingToken);

        // A module row read after its mirror still shows unread in the
        // bell, so pull those read states across now.
        foreach (var global in mirrored)
        {
            var id = ParseId(prefix, global.Type);

            if (id is null || global.IsRead)
                continue;

            var notice = notices.FirstOrDefault(n => ReadNotice(n).Id == id.Value);

            if (notice is not null && ReadNotice(notice).IsRead)
            {
                global.IsRead = true;
                readChanged = true;
            }
        }

        if (readChanged)
            await db.SaveChangesAsync(stoppingToken);

        return added;
    }

    /// <summary>
    /// Marks module rows read where the global mirror row is already
    /// read, so reading a notice in the bell also clears it in the
    /// module's own notification list.
    /// </summary>
    private static async Task PropagateReadsAsync<TNotice>(
        AppDbContext db,
        string prefix,
        List<TNotice> notices,
        CancellationToken stoppingToken)
        where TNotice : class
    {
        var unreadIds = notices
            .Where(n => !ReadNotice(n).IsRead)
            .Select(n => ReadNotice(n).Id)
            .ToHashSet();

        if (unreadIds.Count == 0)
            return;

        var readGlobalTypes = await db.Notifications
            .Where(n => n.Type.StartsWith(prefix) && n.IsRead)
            .Select(n => n.Type)
            .ToListAsync(stoppingToken);

        var idsToMark = readGlobalTypes
            .Select(type => ParseId(prefix, type))
            .Where(id => id.HasValue && unreadIds.Contains(id.Value))
            .Select(id => id!.Value)
            .ToList();

        if (idsToMark.Count == 0)
            return;

        if (prefix == LibraryPrefix)
        {
            await db.LibraryNotifications
                .Where(n => idsToMark.Contains(n.NotificationId))
                .ExecuteUpdateAsync(
                    s => s.SetProperty(n => n.IsRead, true), stoppingToken);
        }
        else if (prefix == LostFoundPrefix)
        {
            await db.LostFoundNotifications
                .Where(n => idsToMark.Contains(n.NotificationId))
                .ExecuteUpdateAsync(
                    s => s.SetProperty(n => n.IsRead, true), stoppingToken);
        }
    }

    // ---- the two supported module rows, projected into one shape ----

    private static (int Id, int UserId, string Title, string Message, bool IsRead, DateTime CreatedAt)
        ReadNotice<TNotice>(TNotice notice) =>
        notice switch
        {
            Models.Library.LibraryNotification library => (
                library.NotificationId,
                library.UserId,
                library.Title,
                library.Message,
                library.IsRead,
                library.CreatedAt),

            Models.LostFound.Notification lostFound => (
                lostFound.NotificationId,
                lostFound.UserId,
                lostFound.Title,
                lostFound.Message,
                lostFound.IsRead,
                lostFound.CreatedAt),

            _ => throw new NotSupportedException(
                $"Unsupported module notification type: {typeof(TNotice).Name}")
        };

    private static int? ParseId(string prefix, string? type)
    {
        if (string.IsNullOrWhiteSpace(type) || !type.StartsWith(prefix))
            return null;

        return int.TryParse(type.Substring(prefix.Length), out var id)
            ? id
            : null;
    }
}
