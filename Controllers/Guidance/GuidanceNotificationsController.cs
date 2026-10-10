using System.Security.Claims;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Services.Guidance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Guidance;

/// <summary>
/// Guidance alerts for the signed-in user (student, faculty or counselor).
/// The user always comes from the JWT - there is no email or user id in the URL.
///   GET   /api/guidance/notifications
///   PATCH /api/guidance/notifications/{id}/read
///   PATCH /api/guidance/notifications/read-all
/// </summary>
[ApiController]
[Route("api/guidance/notifications")]
[Authorize(Roles = GuidanceRoles.Any)]
public class GuidanceNotificationsController : ControllerBase
{
    private readonly GuidanceNotificationService _service;

    public GuidanceNotificationsController(GuidanceNotificationService service)
    {
        _service = service;
    }

    private bool TryGetUserId(out int id) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id);

    private IActionResult Invalid() =>
        Unauthorized(new { message = "Invalid session. Please log in again." });

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!TryGetUserId(out var id)) return Invalid();

        // Polled every few seconds: a browser must never reuse an old answer.
        Response.Headers.CacheControl = "no-store";

        return Ok(await _service.GetAsync(id, ct));
    }

    [HttpPatch("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Invalid();

        return await _service.MarkReadAsync(userId, id, ct)
            ? Ok(new { message = "Notification marked as read." })
            : NotFound(new { message = "Notification not found." });
    }

    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Invalid();

        var count = await _service.MarkAllReadAsync(userId, ct);
        return Ok(new { message = "All notifications marked as read.", count });
    }
}