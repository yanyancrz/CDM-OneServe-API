using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.LostFound;
using CDM_OneServe_API.DTOs.Notifications;
using CDM_OneServe_API.Services.LostFound;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.LostFound;

/// <summary>
/// Lost &amp; Found notifications for the signed-in user, plus
/// the web-push subscription store. Reading and subscriptions are
/// per-user; there is no delivery configuration in this module.
/// </summary>
[ApiController]
[Route("api/lostfound/notifications")]
[Authorize]
public class LostFoundNotificationsController : LostFoundControllerBase
{
    private readonly LostFoundService _service;

    public LostFoundNotificationsController(LostFoundService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetNotifications()
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return Ok(ApiResponse.Ok(await _service.GetNotificationsAsync(userId)));
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        var count = await _service.GetUnreadCountAsync(userId);
        return Ok(ApiResponse.Ok(new { unreadCount = count }));
    }

    [HttpPut("{id:int}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.MarkNotificationReadAsync(userId, id));
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        await _service.MarkAllNotificationsReadAsync(userId);
        return Ok(ApiResponse.Ok("All notifications marked as read."));
    }

    /// <summary>
    /// Stores the browser's web-push subscription (endpoint keyed, so
    /// re-subscribing updates instead of duplicating). The row lands in the
    /// shared PushSubscriptions table, so one subscription here also
    /// receives Library, Guidance and Marketplace push notifications.
    /// </summary>
    [HttpPost("push/subscribe")]
    public async Task<IActionResult> SubscribePush(PushSubscriptionDto dto)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.SubscribePushAsync(
            userId, dto, Request.Headers.UserAgent.ToString()));
    }

    [HttpPost("push/unsubscribe")]
    public async Task<IActionResult> UnsubscribePush(PushSubscriptionDto dto)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();
        return ToAction(await _service.UnsubscribePushAsync(userId, dto));
    }
}
