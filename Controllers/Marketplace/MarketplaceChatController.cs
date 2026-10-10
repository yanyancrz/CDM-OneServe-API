using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// Chat / Concerns, buyer side.
///
///   GET  /api/marketplace/chat/conversation
///   POST /api/marketplace/chat/conversation
///   POST /api/marketplace/chat/conversation/{conversationId}/messages
///
/// There is one thread per buyer, so there is no list here: asking again reopens
/// the same conversation rather than fragmenting the history. The staff inbox is
/// on MarketplaceStaffChatController.
/// </summary>
[ApiController]
[Route("api/marketplace/chat")]
[Authorize]
public class MarketplaceChatController : MarketplaceControllerBase
{
    private readonly MarketplaceChatService _chat;

    public MarketplaceChatController(MarketplaceChatService chat)
    {
        _chat = chat;
    }

    /// <summary>
    /// The buyer's thread with its messages. Opening it marks the staff replies as
    /// read on the staff side, which is how the unread badge clears.
    /// </summary>
    [HttpGet("conversation")]
    public async Task<IActionResult> MyConversation(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _chat.GetMyConversationAsync(userId, ct));
    }

    /// <summary>
    /// Opens the thread with a first message, optionally linked to one of the
    /// buyer's own orders.
    /// </summary>
    [HttpPost("conversation")]
    public async Task<IActionResult> StartConversation(
        [FromBody] StartConversationRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _chat.StartConversationAsync(userId, request, ct));
    }

    /// <summary>Posts into the buyer's own thread. Ownership is checked on the row.</summary>
    [HttpPost("conversation/{conversationId:int}/messages")]
    public async Task<IActionResult> PostMessage(
        int conversationId,
        [FromBody] PostMessageRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _chat.SendMessageAsync(userId, conversationId, request, ct));
    }
}