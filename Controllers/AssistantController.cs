using System.Security.Claims;

using CDM_OneServe_API.Services.Assistant;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers;

/// <summary>
/// Campus assistant proxy. The browser never holds the provider key and never
/// calls the model directly - everything goes through here, authenticated by
/// the existing OneServe JWT.
///
///   POST /api/assistant/ask       { question } -> AI text or a fallback flag
///   POST /api/assistant/feedback  { usageId, helpful } -> thumbs up/down
///
/// Any model outage degrades to the PWA's on-device answers; the assistant
/// never goes dead.
/// </summary>
[ApiController]
[Route("api/assistant")]
[Authorize]
public class AssistantController : ControllerBase
{
    private readonly AssistantService _assistant;

    public AssistantController(AssistantService assistant)
    {
        _assistant = assistant;
    }

    public sealed record AskRequest(string Question = "");
    public sealed record FeedbackRequest(int UsageId, bool Helpful);

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return Unauthorized(new { success = false, message = "Invalid session. Please log in again." });

        var outcome = await _assistant.AskAsync(userId, request.Question ?? "", ct);

        if (!outcome.Asked)
        {
            // 200, not an error status: the client treats this as "use the
            // on-device answers" rather than showing a failure.
            return Ok(new
            {
                success = false,
                fallback = true,
                usageId = outcome.UsageId,
                message = "Instant answers instead."
            });
        }

        return Ok(new
        {
            success = true,
            text = outcome.Text,
            source = outcome.Source,
            latencyMs = outcome.LatencyMs,
            usageId = outcome.UsageId
        });
    }

    [HttpPost("feedback")]
    public async Task<IActionResult> Feedback([FromBody] FeedbackRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return Unauthorized(new { success = false, message = "Invalid session. Please log in again." });

        var saved = await _assistant.FeedbackAsync(userId, request.UsageId, request.Helpful, ct);

        return saved
            ? Ok(new { success = true })
            : NotFound(new { success = false, message = "Answer not found." });
    }
}
