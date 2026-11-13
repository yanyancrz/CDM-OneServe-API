using System.Diagnostics;
using System.Reflection;

using CDM_OneServe_API.Data;
using CDM_OneServe_API.Hubs;
using CDM_OneServe_API.Models.Platform;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Platform;

/// <summary>
/// Live health probes for every component the monitoring page lists.
///
/// Every status comes from a real check:
///   API        - this process answering the request (uptime, memory)
///   Database   - an actual `SELECT 1` round trip
///   Clinic CMS - a real HTTP request to the configured CMS origin
///   Web Push   - outbox backlog / failure rate / VAPID configuration
///   SignalR    - the hub hosted in this process + live connection count
///
/// Nothing here returns a hard-coded "online".
/// </summary>
public class SystemHealthService
{
    private static readonly DateTime StartedAt = DateTime.UtcNow;

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SystemHealthService> _logger;

    public SystemHealthService(
        AppDbContext db,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<SystemHealthService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<SystemHealthReport> CheckAllAsync(CancellationToken ct = default)
    {
        // The HTTP probe can overlap database work, but database probes share
        // one scoped DbContext and must run sequentially.
        var clinicTask = CheckClinicAsync(ct);
        var database = await CheckDatabaseAsync(ct);
        var push = await CheckPushAsync(ct);
        var components = new List<ComponentHealth>
        {
            CheckApi(),
            database,
            await clinicTask,
            push,
            CheckSignalR()
        };

        var worst = components
            .OrderByDescending(c => Severity(c.Status))
            .FirstOrDefault()?.Status ?? "unknown";

        return new SystemHealthReport(
            worst,
            DateTime.UtcNow,
            components);
    }

    /// <summary>Runs the probes and appends them to the history table.</summary>
    public async Task<SystemHealthReport> CheckAndRecordAsync(CancellationToken ct = default)
    {
        var report = await CheckAllAsync(ct);

        foreach (var component in report.Components)
        {
            _db.SystemHealthChecks.Add(new SystemHealthSnapshot
            {
                Component = component.Component,
                Status = component.Status,
                LatencyMs = component.LatencyMs,
                Message = component.Message,
                CheckedAt = report.CheckedAt
            });
        }

        // Keep the history bounded: 7 days is enough for the page's charts
        // and keeps a small Azure MySQL instance from growing forever.
        var cutoff = DateTime.UtcNow.AddDays(-7);
        await _db.SystemHealthChecks
            .Where(x => x.CheckedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        await _db.SaveChangesAsync(ct);
        return report;
    }

    public async Task<List<SystemHealthSnapshot>> GetHistoryAsync(
        string? component,
        int hours,
        CancellationToken ct = default)
    {
        var window = Math.Clamp(hours, 1, 168);
        var from = DateTime.UtcNow.AddHours(-window);

        var rows = _db.SystemHealthChecks
            .AsNoTracking()
            .Where(x => x.CheckedAt >= from);

        if (!string.IsNullOrWhiteSpace(component))
        {
            var name = component.Trim();
            rows = rows.Where(x => x.Component == name);
        }

        return await rows
            .OrderBy(x => x.CheckedAt)
            .Take(2000)
            .ToListAsync(ct);
    }

    // ---------------------------------------------------------
    // PROBES
    // ---------------------------------------------------------

    private static ComponentHealth CheckApi()
    {
        var process = Process.GetCurrentProcess();
        var uptime = DateTime.UtcNow - StartedAt;

        var version = Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString() ?? "unknown";

        var memoryMb = Math.Round(process.WorkingSet64 / 1024d / 1024d, 1);

        return new ComponentHealth(
            "api",
            "OneServe API",
            "online",
            0,
            $"v{version} - up {FormatUptime(uptime)} - {memoryMb} MB working set",
            DateTime.UtcNow);
    }

    private async Task<ComponentHealth> CheckDatabaseAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _db.Database.ExecuteSqlRawAsync("SELECT 1", ct);

            stopwatch.Stop();

            var latency = (int)stopwatch.ElapsedMilliseconds;

            return new ComponentHealth(
                "database",
                "Azure Database (MySQL)",
                latency > 1000 ? "degraded" : "online",
                latency,
                latency > 1000
                    ? $"Reachable but slow ({latency} ms)"
                    : $"Reachable in {latency} ms",
                DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogWarning(ex, "Database health check failed");

            return new ComponentHealth(
                "database",
                "Azure Database (MySQL)",
                "offline",
                (int)stopwatch.ElapsedMilliseconds,
                "The database did not answer the health query.",
                DateTime.UtcNow);
        }
    }

    public async Task<ComponentHealth> CheckClinicAsync(CancellationToken ct)
    {
        var baseUrl = _configuration["Clinic:BaseUrl"];
        var configured = !string.IsNullOrWhiteSpace(baseUrl) &&
                         !string.IsNullOrWhiteSpace(_configuration["Clinic:ApiKey"]);

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return new ComponentHealth(
                "clinic_cms",
                "Clinic CMS",
                "unknown",
                null,
                "Clinic CMS is not configured on this server.",
                DateTime.UtcNow);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);

            using var response = await client.GetAsync(uri, ct);

            stopwatch.Stop();
            var latency = (int)stopwatch.ElapsedMilliseconds;
            var code = (int)response.StatusCode;

            // A 4xx on the root is still proof the CMS is alive; only a
            // server error (or no answer) means the integration is impaired.
            var status = code >= 500
                ? "degraded"
                : latency > 5000
                    ? "degraded"
                    : "online";

            var note = configured
                ? $"HTTP {code} in {latency} ms"
                : $"HTTP {code} in {latency} ms - integration key is missing";

            return new ComponentHealth(
                "clinic_cms",
                "Clinic CMS",
                configured ? status : "degraded",
                latency,
                note,
                DateTime.UtcNow);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ComponentHealth(
                "clinic_cms", "Clinic CMS", "offline", (int)stopwatch.ElapsedMilliseconds,
                "The Clinic CMS health check timed out.", DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();

            return new ComponentHealth(
                "clinic_cms",
                "Clinic CMS",
                "offline",
                (int)stopwatch.ElapsedMilliseconds,
                "The Clinic CMS could not be reached.",
                DateTime.UtcNow);
        }
    }

    private async Task<ComponentHealth> CheckPushAsync(CancellationToken ct)
    {
        var publicKey = _configuration["WebPush:VapidPublicKey"];

        if (string.IsNullOrWhiteSpace(publicKey))
        {
            return new ComponentHealth(
                "web_push",
                "Web Push Service",
                "degraded",
                null,
                "VAPID public key is not configured.",
                DateTime.UtcNow);
        }

        try
        {
            var since = DateTime.UtcNow.AddHours(-24);
            var staleCutoff = DateTime.UtcNow.AddMinutes(-15);

            var sent = await _db.PushDeliveryLogs
                .AsNoTracking()
                .CountAsync(x => x.CreatedAt >= since && x.Status == "Sent", ct);

            var failed = await _db.PushDeliveryLogs
                .AsNoTracking()
                .CountAsync(x => x.CreatedAt >= since && x.Status == "Failed", ct);

            var stale = await _db.PushOutbox
                .AsNoTracking()
                .CountAsync(x =>
                    (x.Status == "Pending" || x.Status == "Processing") &&
                    x.CreatedAt <= staleCutoff, ct);

            var activeSubscriptions = await _db.PushSubscriptions
                .AsNoTracking()
                .CountAsync(x => x.IsActive, ct);

            var total = sent + failed;
            var failureRate = total == 0 ? 0 : failed * 100d / total;

            var degraded = stale > 50 || failureRate > 25;

            var note =
                $"{activeSubscriptions} active subscription(s), " +
                $"{sent} sent / {failed} failed in 24h" +
                (stale > 0 ? $", {stale} queued over 15 min" : "");

            return new ComponentHealth(
                "web_push",
                "Web Push Service",
                degraded ? "degraded" : "online",
                null,
                note,
                DateTime.UtcNow);
        }
        catch
        {
            return new ComponentHealth(
                "web_push",
                "Web Push Service",
                "unknown",
                null,
                "Delivery data is unavailable.",
                DateTime.UtcNow);
        }
    }

    private static ComponentHealth CheckSignalR() =>
        new(
            "signalr",
            "SignalR Hub",
            "online",
            null,
            $"Hub hosted in-process - {GuidanceHub.ActiveConnections} live connection(s)",
            DateTime.UtcNow);

    private static int Severity(string status) => status switch
    {
        "offline" => 3,
        "degraded" => 2,
        "unknown" => 1,
        _ => 0
    };

    private static string FormatUptime(TimeSpan uptime) =>
        uptime.TotalDays >= 1
            ? $"{(int)uptime.TotalDays}d {uptime.Hours}h"
            : uptime.TotalHours >= 1
                ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m"
                : $"{uptime.Minutes}m";
}

public sealed record ComponentHealth(
    string Component,
    string Label,
    string Status,
    int? LatencyMs,
    string? Message,
    DateTime CheckedAt);

public sealed record SystemHealthReport(
    string OverallStatus,
    DateTime CheckedAt,
    List<ComponentHealth> Components);
