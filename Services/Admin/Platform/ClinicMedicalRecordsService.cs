using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CDM_OneServe_API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CDM_OneServe_API.Services.Admin.Platform;

public sealed class ClinicMedicalRecordsService(
    AppDbContext db, IHttpClientFactory clients, IConfiguration configuration, IMemoryCache cache)
{
    public async Task<JsonElement> GetRecordsAsync(string studentNumber, CancellationToken ct)
    {
        if (!Uri.TryCreate(configuration["Clinic:BaseUrl"], UriKind.Absolute, out var baseUri)
            || string.IsNullOrWhiteSpace(configuration["Clinic:ApiKey"]))
            throw new HttpRequestException("Clinic integration is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(baseUri, $"api/integration/student/{Uri.EscapeDataString(studentNumber.Trim())}/medical-records"));
        request.Headers.Add("X-Integration-Key", configuration["Clinic:ApiKey"]);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await clients.CreateClient().SendAsync(request, timeout.Token);
        // Match the mobile API: an upstream error is never an empty record list.
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Clinic records could not be retrieved.", null, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        if (document.RootElement.ValueKind != JsonValueKind.Array
            || document.RootElement.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.Object))
            throw new JsonException("Invalid Clinic records response.");
        return document.RootElement.Clone();
    }

    public async Task<ClinicMedicalCount> GetCountAsync(CancellationToken ct)
    {
        if (cache.TryGetValue<ClinicMedicalCount>("clinic.medical-count", out var cached) && cached is not null)
            return cached;

        var students = await db.Users.AsNoTracking()
            .Where(u => u.Role == "Student" && u.AccountStatus != "Deleted" && u.IdNumber != "")
            .Select(u => new { u.Id, u.IdNumber }).ToListAsync(ct);
        var count = 0;
        var failures = 0;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            await Parallel.ForEachAsync(students,
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = deadline.Token },
                async (student, token) =>
                {
                    try
                    {
                        var records = await GetRecordsAsync(student.IdNumber, token);
                        if (records.GetArrayLength() > 0) Interlocked.Increment(ref count);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
                    {
                        Interlocked.Increment(ref failures);
                    }
                });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            failures++;
        }
        ct.ThrowIfCancellationRequested();
        var result = new ClinicMedicalCount(failures == 0 ? count : null,
            failures == 0 ? "Students with medical records in the Clinic CMS."
                : "Clinic count unavailable: some medical records could not be checked.", DateTime.UtcNow);
        cache.Set("clinic.medical-count", result, TimeSpan.FromSeconds(failures == 0 ? 120 : 15));
        return result;
    }
}

public sealed record ClinicMedicalCount(int? Count, string Note, DateTime CheckedAt);
