using System.Security.Claims;
using CDM_OneServe_API.DTOs.Notifications;
using CDM_OneServe_API.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers;

/// <summary>
/// Web Push (RFC 8030 / VAPID) subscription management.
///
/// Every action is <c>[Authorize]</c>-ed and reads the user id from the
/// OneServe JWT, so a subscription can only ever be created against, or
/// read from, the account that owns the token - ownership is never taken
/// from the request body.
///
/// Sending belongs to the Node notification service; this controller only
/// manages registration, status and the outbox trigger.
/// </summary>
[ApiController]
[Route("api/push")]
[Authorize]
public class PushController : ControllerBase
{
    private readonly PushSubscriptionService _subscriptions;
    private readonly IPushDispatcher _dispatcher;
    private readonly IConfiguration _configuration;

    public PushController(
        PushSubscriptionService subscriptions,
        IPushDispatcher dispatcher,
        IConfiguration configuration)
    {
        _subscriptions = subscriptions;
        _dispatcher = dispatcher;
        _configuration = configuration;
    }

    /// <summary>
    /// The VAPID public key. Safe to be public: it is the key the browser
    /// uses to prove a notification really came from this server, and it
    /// is published in every VAPID token. The private key never leaves
    /// the Node notification service.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("vapid-public-key")]
    public IActionResult GetVapidPublicKey()
    {
        var publicKey = _configuration["WebPush:VapidPublicKey"];

        if (string.IsNullOrWhiteSpace(publicKey))
        {
            return StatusCode(503, new
            {
                success = false,
                message = "Web Push is not configured on this server."
            });
        }

        return Ok(new { success = true, publicKey });
    }

    /// <summary>Stores the browser's PushSubscription for the current user.</summary>
    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe(
        PushSubscriptionDto dto,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return UnauthorizedSession();

        var (success, message, subscription) =
            await _subscriptions.SubscribeAsync(userId, dto, Request.Headers.UserAgent.ToString(), ct);

        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new
        {
            success = true,
            message,
            subscription
        });
    }

    /// <summary>Removes one of the current user's subscriptions.</summary>
    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe(PushUnsubscribeDto dto, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return UnauthorizedSession();

        var (success, message, notFound) = await _subscriptions.UnsubscribeAsync(userId, dto, ct);

        // 404 rather than 403: never reveal whether an id belongs to someone else.
        if (notFound)
            return NotFound(new { success = false, message });

        if (!success)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message });
    }

    /// <summary>
    /// The current user's devices, with the live/expired flag. Used by
    /// the UI to show "notifications on" without asking the browser.
    /// </summary>
    [HttpGet("subscriptions")]
    public async Task<IActionResult> MySubscriptions(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return UnauthorizedSession();

        var subscriptions = await _subscriptions.ListAsync(userId, ct);

        return Ok(new
        {
            success = true,
            hasActiveSubscription = subscriptions.Any(s => s.IsActive),
            subscriptions
        });
    }

    /// <summary>
    /// Re-checks the browser's subscription against the server and
    /// repairs it if the keys rotated, the row expired, or the browser
    /// re-subscribed after <c>pushsubscriptionchange</c>. Also the hook the
    /// service worker's <c>pushsubscriptionchange</c> event calls.
    /// </summary>
    [HttpPost("validate")]
    public async Task<IActionResult> Validate(PushSubscriptionDto? dto, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return UnauthorizedSession();

        if (dto is not null && !string.IsNullOrWhiteSpace(dto.Endpoint))
        {
            var (success, message, _) =
                await _subscriptions.SubscribeAsync(userId, dto, Request.Headers.UserAgent.ToString(), ct);

            if (!success)
                return BadRequest(new { success = false, message });
        }

        var subscriptions = await _subscriptions.ListAsync(userId, ct);

        return Ok(new
        {
            success = true,
            hasActiveSubscription = subscriptions.Any(s => s.IsActive),
            subscriptions
        });
    }

    /// <summary>
    /// Sends a real notification to the caller's own browsers so they can
    /// confirm the whole chain (VAPID -&gt; push service -&gt; service worker)
    /// works before they trust it.
    /// </summary>
    [HttpPost("test")]
    public async Task<IActionResult> SendTest(int? pushSubscriptionId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return UnauthorizedSession();

        var queued = await _dispatcher.EnqueueTestAsync(userId, pushSubscriptionId, ct);

        await _dispatcher.FlushAsync(ct);

        if (!queued)
        {
            return BadRequest(new
            {
                success = false,
                message = "This device has no active notification subscription yet."
            });
        }

        return Ok(new
        {
            success = true,
            message = "Test notification queued. It should arrive in a few seconds."
        });
    }

    private IActionResult UnauthorizedSession() => Unauthorized(new
    {
        success = false,
        message = "Invalid session. Please log in again."
    });

    private bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;
}
