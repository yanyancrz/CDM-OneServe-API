using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models.Platform;
using CDM_OneServe_API.Models;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Platform;

/// <summary>
/// Records Clinic CMS calls for the integration page.
///
/// The integration key, the student number and the medical payload are never
/// written. Only the resource name, the path shape, the status, the duration
/// and a sanitised error message are kept.
/// </summary>
public class ClinicIntegrationLogger
{
    private readonly AppDbContext _db;
    private readonly ILogger<ClinicIntegrationLogger> _logger;

    public ClinicIntegrationLogger(
        AppDbContext db,
        ILogger<ClinicIntegrationLogger> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogAsync(
        int? userId,
        string resource,
        string endpoint,
        int? statusCode,
        int durationMs,
        bool success,
        string? errorMessage,
        CancellationToken ct = default)
    {
        try
        {
            _db.ClinicIntegrationLogs.Add(new ClinicIntegrationLog
            {
                UserId = userId,
                Resource = (resource ?? "").Trim(),
                Method = "GET",
                Endpoint = SanitizeEndpoint(endpoint),
                StatusCode = statusCode,
                DurationMs = durationMs,
                Success = success,
                ErrorMessage = SanitizeError(errorMessage),
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record Clinic integration log");
        }
    }

    /// <summary>
    /// Drops the student number so the log cannot be used to enumerate which
    /// patient was fetched, and caps the path length.
    /// </summary>
    private static string SanitizeEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return "";

        var text = endpoint.Trim();

        // Keep only the shape: api/integration/student/{id}/{resource}
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"student/[^/]+",
            "student/{id}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return text.Length <= 300 ? text : text[..300];
    }

    private static string? SanitizeError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return null;

        var text = error.Trim();

        return text.Length <= 300 ? text : text[..300];
    }

    // ---------------------------------------------------------
    // SUMMARY (Clinic service page)
    // ---------------------------------------------------------

    public async Task<ClinicSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var day = now.AddHours(-24);
        var week = now.AddDays(-7);

        var total = await _db.ClinicIntegrationLogs
            .AsNoTracking()
            .CountAsync(x => x.CreatedAt >= day, ct);

        var successes = await _db.ClinicIntegrationLogs
            .AsNoTracking()
            .CountAsync(x => x.CreatedAt >= day && x.Success, ct);

        var failures = total - successes;

        var avgLatency = total == 0
            ? (double?)null
            : await _db.ClinicIntegrationLogs
                .AsNoTracking()
                .Where(x => x.CreatedAt >= day)
                .AverageAsync(x => (double)x.DurationMs, ct);

        var lastSuccess = await _db.ClinicIntegrationLogs
            .AsNoTracking()
            .Where(x => x.Success)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => (DateTime?)x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var lastFailure = await _db.ClinicIntegrationLogs
            .AsNoTracking()
            .Where(x => !x.Success)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => (DateTime?)x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var studentsWithRecords = await _db.UserActivities
            .AsNoTracking()
            .Where(a => a.Action == UserActivityActions.ClinicRecordsViewed)
            .Select(a => a.UserId)
            .Distinct()
            .CountAsync(ct);

        var viewedThisWeek = await _db.UserActivities
            .AsNoTracking()
            .Where(a => a.Action == UserActivityActions.ClinicRecordsViewed &&
                        a.CreatedAt >= week)
            .Select(a => a.UserId)
            .Distinct()
            .CountAsync(ct);

        var recent = await _db.ClinicIntegrationLogs
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(25)
            .ToListAsync(ct);

        var failuresByResource = await _db.ClinicIntegrationLogs
            .AsNoTracking()
            .Where(x => x.CreatedAt >= week && !x.Success)
            .GroupBy(x => x.Resource)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new ClinicFailureGroup(g.Key, g.Count()))
            .ToListAsync(ct);

        return new ClinicSummary(
            Calls24h: total,
            Success24h: successes,
            Failed24h: failures,
            SuccessRate24h: total == 0 ? null : Math.Round(successes * 100d / total, 1),
            AverageLatencyMs: avgLatency is null ? null : Math.Round(avgLatency.Value, 0),
            LastSuccessAt: lastSuccess,
            LastFailureAt: lastFailure,
            StudentsWithRecords: studentsWithRecords,
            StudentsActiveThisWeek: viewedThisWeek,
            RecentCalls: recent,
            FailuresByResource: failuresByResource,
            CheckedAt: now);
    }

    public async Task<List<ClinicIntegrationLog>> QueryLogsAsync(
        bool? success,
        string? resource,
        bool? failuresOnly,
        DateTime? from,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var rows = _db.ClinicIntegrationLogs.AsNoTracking();

        if (success is { } ok) rows = rows.Where(x => x.Success == ok);
        if (failuresOnly == true) rows = rows.Where(x => !x.Success);

        if (!string.IsNullOrWhiteSpace(resource))
        {
            var name = resource.Trim();
            rows = rows.Where(x => x.Resource == name);
        }

        if (from is { } start) rows = rows.Where(x => x.CreatedAt >= start);

        return await rows
            .OrderByDescending(x => x.CreatedAt)
            .Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 10, 100))
            .Take(Math.Clamp(pageSize, 10, 100))
            .ToListAsync(ct);
    }
}

public sealed record ClinicSummary(
    int Calls24h,
    int Success24h,
    int Failed24h,
    double? SuccessRate24h,
    double? AverageLatencyMs,
    DateTime? LastSuccessAt,
    DateTime? LastFailureAt,
    int StudentsWithRecords,
    int StudentsActiveThisWeek,
    List<ClinicIntegrationLog> RecentCalls,
    List<ClinicFailureGroup> FailuresByResource,
    DateTime CheckedAt);

public sealed record ClinicFailureGroup(string Resource, int Count);
