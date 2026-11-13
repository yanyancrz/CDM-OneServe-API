using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers;

[ApiController]
[Route("api/clinic/me")]
[Authorize]
public class ClinicController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ClinicController> _logger;


    public ClinicController(
    AppDbContext db,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ClinicController> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("{resource}")]
    public async Task<IActionResult> GetMyClinicRecords(
        string resource,
        CancellationToken cancellationToken)
    {
        var allowedResources = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "medical-records",
            "vital-signs",
            "dental-records",
            "referrals"
        };

        if (!allowedResources.Contains(resource))
            return NotFound();

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
            return Unauthorized();

        if (!string.Equals(user.AccountStatus, "Active",
        StringComparison.OrdinalIgnoreCase))
            return Forbid();

        if (!string.Equals(user.Role, "Student",
                StringComparison.OrdinalIgnoreCase))
            return Forbid();

        // Use the authenticated OneServe user's ID number
        // as the CMS student number.
        var studentNumber = user.IdNumber?.Trim();

        if (string.IsNullOrWhiteSpace(studentNumber))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Clinic access has not been configured for this account." });

        var cmsBaseUrl = _configuration["Clinic:BaseUrl"];
        var integrationKey = _configuration["Clinic:ApiKey"];

        if (!Uri.TryCreate(cmsBaseUrl, UriKind.Absolute, out var baseUri)
            || string.IsNullOrWhiteSpace(integrationKey))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Clinic integration is not configured." });
        }

        var client = _httpClientFactory.CreateClient();
        var endpoint =
            $"api/integration/student/{Uri.EscapeDataString(studentNumber)}/{resource}";

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(baseUri, endpoint));

        request.Headers.Add("X-Integration-Key", integrationKey);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var content = await response.Content.ReadAsStringAsync(
                cancellationToken);

            // The dashboard has no direct access to the Clinic CMS, so remember
            // the first time a student actually has a medical record there.
            if (response.IsSuccessStatusCode
                && string.Equals(resource, "medical-records",
                    StringComparison.OrdinalIgnoreCase)
                && HasNonEmptyArray(content))
            {
                try
                {
                    await RecordClinicUsageAsync(user.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Never fail the student's request because logging failed.
                    _logger.LogWarning(
                        ex,
                        "Could not record clinic usage for user {UserId}",
                        user.Id);
                }
            }

            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                Content = content,
                ContentType = response.Content.Headers.ContentType?.ToString()
                    ?? "application/json"
            };
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "Clinic CMS request failed for resource: {Resource}",
                resource);

            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "The Clinic CMS could not be reached." });
        }
    }

    private static bool HasNonEmptyArray(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);

            return document.RootElement.ValueKind == JsonValueKind.Array
                && document.RootElement.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task RecordClinicUsageAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var alreadyRecorded = await _db.UserActivities
            .AsNoTracking()
            .AnyAsync(
                a => a.UserId == userId
                    && a.Action == UserActivityActions.ClinicRecordsViewed,
                cancellationToken);

        if (alreadyRecorded)
            return;

        _db.UserActivities.Add(new UserActivity
        {
            UserId = userId,
            Action = UserActivityActions.ClinicRecordsViewed,
            Description = "Viewed clinic medical records",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
    }
}