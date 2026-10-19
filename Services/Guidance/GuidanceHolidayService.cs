using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CDM_OneServe_API.Services.Guidance;

/// <summary>
/// Philippine public holidays - THE one server-side source of truth for
/// "is this date closed to booking because of a holiday".
///
/// The list comes from the same public API the mobile app used to call directly
/// (Nager.Date, no key, no signup) so the calendar a student sees and the rule
/// the server enforces can never disagree. Results are cached in memory per
/// calendar year for a day, and every failure is swallowed: an outage of the
/// holiday provider must never block booking, it just means "no holidays known".
/// </summary>
public class GuidanceHolidayService
{
    private const string UrlTemplate = "https://date.nager.at/api/v3/PublicHolidays/{0}/PH";
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(30);

    // Static so the cache survives across requests (the service itself is scoped).
    private static readonly ConcurrentDictionary<int, Entry> Cache = new();
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly IHttpClientFactory _http;
    private readonly ILogger<GuidanceHolidayService> _logger;

    public GuidanceHolidayService(IHttpClientFactory http, ILogger<GuidanceHolidayService> logger)
    {
        _http = http;
        _logger = logger;
    }

    private sealed record Entry(DateTime FetchedAtUtc, IReadOnlyDictionary<string, string>? Map, bool Ok);

    /// <summary>{ "2026-01-01": "New Year's Day", ... } for the year. Never throws.</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetHolidaysAsync(int year, CancellationToken ct = default)
    {
        if (TryFresh(year, out var cached)) return cached!;

        await Gate.WaitAsync(ct);
        try
        {
            if (TryFresh(year, out var again)) return again!;

            var map = await FetchAsync(year, ct);

            Cache[year] = map != null
                ? new Entry(DateTime.UtcNow, map, true)
                : new Entry(DateTime.UtcNow, Empty, false);

            return Cache[year].Map!;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<string?> GetHolidayNameAsync(DateOnly date, CancellationToken ct = default)
    {
        var map = await GetHolidaysAsync(date.Year, ct);
        var key = date.ToString("yyyy-MM-dd");
        return map.TryGetValue(key, out var name) ? name : null;
    }

    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>();

    private static bool TryFresh(int year, out IReadOnlyDictionary<string, string>? map)
    {
        map = null;
        if (!Cache.TryGetValue(year, out var entry)) return false;

        var age = DateTime.UtcNow - entry.FetchedAtUtc;
        if (age >= (entry.Ok ? SuccessTtl : FailureTtl)) return false;

        map = entry.Map;
        return true;
    }

    private async Task<IReadOnlyDictionary<string, string>?> FetchAsync(int year, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var client = _http.CreateClient(nameof(GuidanceHolidayService));
            var rows = await client.GetFromJsonAsync<List<NagerHoliday>>(
                string.Format(UrlTemplate, year), cts.Token);

            var map = new Dictionary<string, string>();
            foreach (var h in rows ?? new List<NagerHoliday>())
            {
                if (string.IsNullOrWhiteSpace(h.Date)) continue;
                map[h.Date] = string.IsNullOrWhiteSpace(h.LocalName)
                    ? (string.IsNullOrWhiteSpace(h.Name) ? "Public holiday" : h.Name!)
                    : h.LocalName!;
            }

            return map;
        }
        catch (Exception ex)
        {
            // Holiday lookup is best-effort. Booking keeps working without it.
            _logger.LogWarning(ex, "Guidance: could not load PH holidays for {Year}.", year);
            return null;
        }
    }

    private sealed class NagerHoliday
    {
        [JsonPropertyName("date")] public string? Date { get; set; }
        [JsonPropertyName("localName")] public string? LocalName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }
}
