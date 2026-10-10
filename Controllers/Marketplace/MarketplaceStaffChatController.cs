using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// Chat / Concerns, staff side - the single unified inbox.
///
///   GET  /api/marketplace/staff/chat/conversations
///   GET  /api/marketplace/staff/chat/conversations/{conversationId}
///   POST /api/marketplace/staff/chat/conversations/{conversationId}/reply
///   PATCH /api/marketplace/staff/chat/conversations/{conversationId}/status
///
/// Because there is exactly ONE Marketplace Staff account, every buyer's thread
/// lands in this one list.
///
/// The CDM OneServe Admin is NOT a participant here. They cannot read these
/// messages and cannot reply as staff - every method resolves the caller through
/// MarketplaceAccessService.GetStaffAsync, which only accepts a row in
/// marketplace_staff.
/// </summary>
[ApiController]
[Route("api/marketplace/staff/chat")]
[Authorize]
public class MarketplaceStaffChatController : MarketplaceControllerBase
{
    private readonly MarketplaceChatService _chat;

    public MarketplaceStaffChatController(MarketplaceChatService chat)
    {
        _chat = chat;
    }

    /// <summary>
    /// Every conversation, newest activity first, each with an unread count of
    /// messages still waiting on staff.
    /// </summary>
    [HttpGet("conversations")]
    public async Task<IActionResult> Conversations(
        [FromQuery] string? status,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _chat.ListForStaffAsync(userId, status, ct));
    }

    /// <summary>
    /// One thread in full. Opening it marks the buyer's messages read, which is
    /// how the badge clears.
    /// </summary>
    [HttpGet("conversations/{conversationId:int}")]
    public async Task<IActionResult> Conversation(int conversationId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _chat.GetForStaffAsync(userId, conversationId, ct));
    }

    /// <summary>
    /// Replies as Marketplace Staff. The sender type is hard-coded to `staff` in
    /// the service - there is no request field that could make it anything else.
    /// </summary>
    [HttpPost("conversations/{conversationId:int}/reply")]
    public async Task<IActionResult> Reply(
        int conversationId,
        [FromBody] PostMessageRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _chat.ReplyAsync(userId, conversationId, request, ct));
    }

    /// <summary>Moves the thread to open / in_progress / resolved.</summary>
    [HttpPatch("conversations/{conversationId:int}/status")]
    public async Task<IActionResult> UpdateStatus(
        int conversationId,
        [FromBody] UpdateConversationStatusRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _chat.UpdateStatusAsync(userId, conversationId, request.Status, ct));
    }
}