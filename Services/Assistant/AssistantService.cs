using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using CDM_OneServe_API.Data;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Assistant;

/// <summary>
/// Talks to the Gemini API behind a backend proxy, so the key never reaches
/// the browser or the repo. Reads GEMINI_API_KEY from the environment first
/// (production), then appsettings (local dev only - never commit a real key).
///
/// Every failure mode (no key, quota/429, timeout, blocked content) returns
/// Asked=false, and the PWA silently falls back to its on-device answers -
/// the assistant never goes dead.
/// </summary>
public class AssistantService
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;

    // Per-user daily ask count. In-memory on purpose: losing it on restart
    // only resets a quota, it can never lock anyone out or corrupt data.
    private static readonly ConcurrentDictionary<string, int> DailyCounts = new();

    public AssistantService(AppDbContext db, IHttpClientFactory http, IConfiguration config)
    {
        _db = db;
        _http = http;
        _config = config;
    }

    public sealed record AskOutcome(
        bool Asked,
        string Text,
        string Source,
        int LatencyMs,
        int? UsageId,
        string? Error);

    public async Task<AskOutcome> AskAsync(
        int userId, string question, CancellationToken ct = default)
    {
        var started = DateTime.UtcNow;
        var clean = (question ?? "").Trim();

        if (clean.Length == 0)
            return await LogAsync(userId, clean, 0, "fallback", "Empty question.", ct);

        if (clean.Length > 500)
            clean = clean[..500];

        var section = _config.GetSection("Assistant");
        var model = section["Model"];
        if (string.IsNullOrWhiteSpace(model)) model = "gemini-2.5-flash";

        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? section["ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
            return await LogAsync(userId, clean, Elapsed(started), "fallback", "No API key configured.", ct);

        var dailyLimit = section.GetValue("DailyLimitPerUser", 50);
        var bucket = $"{userId}:{DateTime.UtcNow:yyyyMMdd}";
        var used = DailyCounts.AddOrUpdate(bucket, 1, (_, n) => n + 1);

        // Opportunistic prune so the map cannot grow across days.
        if (used == 1)
        {
            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            foreach (var key in DailyCounts.Keys.Where(k => !k.EndsWith(today)).ToList())
                DailyCounts.TryRemove(key, out _);
        }

        if (used > dailyLimit)
            return await LogAsync(userId, clean, Elapsed(started), "fallback", "Daily ask limit reached.", ct);

        var timeoutSeconds = section.GetValue("TimeoutSeconds", 20);
        var maxTokens = section.GetValue("MaxOutputTokens", 300);

        try
        {
            var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 60));

            var body = new
            {
                system_instruction = new { parts = new[] { new { text = AssistantKnowledge.BuildSystemPrompt() } } },
                contents = new[] { new { role = "user", parts = new[] { new { text = clean } } } },
                generationConfig = new { temperature = 0.3, maxOutputTokens = maxTokens }
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };

            // Header, not ?key= query: keys in URLs end up in server access logs.
            request.Headers.Add("x-goog-api-key", apiKey);

            using var response = await client.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return await LogAsync(userId, clean, Elapsed(started), "fallback",
                    $"Provider {(int)response.StatusCode}.", ct);

            var text = ExtractText(json);

            if (string.IsNullOrWhiteSpace(text))
                return await LogAsync(userId, clean, Elapsed(started), "fallback", "Empty model reply.", ct);

            if (text.Length > 1200) text = text[..1200];

            return await LogAsync(userId, clean, Elapsed(started), "ai", null, ct, text);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return await LogAsync(userId, clean, Elapsed(started), "fallback", "Provider timeout.", ct);
        }
        catch (Exception ex)
        {
            return await LogAsync(userId, clean, Elapsed(started), "fallback",
                ex.Message.Length > 200 ? ex.Message[..200] : ex.Message, ct);
        }
    }

    public async Task<bool> FeedbackAsync(int userId, int usageId, bool helpful, CancellationToken ct = default)
    {
        var updated = await _db.AssistantUsages
            .Where(u => u.UsageId == usageId && u.UserId == userId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Helpful, helpful), ct);

        return updated == 1;
    }

    private static string ExtractText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("promptFeedback", out var feedback) &&
                feedback.TryGetProperty("blockReason", out _))
                return "";

            var parts = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")
                .EnumerateArray();

            var sb = new StringBuilder();
            foreach (var part in parts)
                if (part.TryGetProperty("text", out var text))
                    sb.Append(text.GetString());

            return sb.ToString().Trim();
        }
        catch
        {
            return "";
        }
    }

    private static int Elapsed(DateTime started) =>
        Math.Max(0, (int)(DateTime.UtcNow - started).TotalMilliseconds);

    /// <summary>
    /// Records the outcome for the research evaluation. Logging must never
    /// break an answer, so any failure here is swallowed after the outcome
    /// is already decided.
    /// </summary>
    private async Task<AskOutcome> LogAsync(
        int userId, string question, int latencyMs, string source,
        string? error, CancellationToken ct, string text = "")
    {
        int? usageId = null;

        try
        {
            var row = new Models.AssistantUsage
            {
                UserId = userId,
                Question = question.Length > 300 ? question[..300] : question,
                LatencyMs = latencyMs,
                Source = source,
                Error = error,
                CreatedAt = DateTime.UtcNow,
            };

            _db.AssistantUsages.Add(row);
            await _db.SaveChangesAsync(ct);
            usageId = row.UsageId;
        }
        catch
        {
            // Evaluation logging is expendable; the answer is not.
        }

        return new AskOutcome(
            Asked: source == "ai",
            Text: text,
            Source: source,
            LatencyMs: latencyMs,
            UsageId: usageId,
            Error: error);
    }
}
