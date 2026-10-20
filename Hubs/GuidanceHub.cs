using System.Security.Claims;

using CDM_OneServe_API.Data;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Hubs;

/// <summary>
/// Real-time channel for Guidance. Every authenticated user joins the
/// "user:{id}" group so the server can push chat messages and notifications
/// to that specific user the moment they happen.
/// Clients may also join a conversation group to watch a thread live.
/// </summary>
[Authorize]
public class GuidanceHub : Hub
{
    private readonly AppDbContext _context;

    public GuidanceHub(AppDbContext context)
    {
        _context = context;
    }

    public override async Task OnConnectedAsync()
    {
        if (TryUserId(Context.User, out var id))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{id}");

        await base.OnConnectedAsync();
    }

    public async Task JoinConversation(int conversationId)
    {
        if (!TryUserId(Context.User, out var userId))
            throw new HubException("You must be signed in.");

        // A conversation is only ever between one student and one counselor.
        // Never trust the client's conversation id without checking that
        // this connection belongs to one of the two participants.
        var isParticipant = await _context.GuidanceChatConversations
            .AsNoTracking()
            .AnyAsync(c =>
                c.Id == conversationId &&
                (c.StudentId == userId || c.CounselorId == userId));

        if (!isParticipant)
            throw new HubException("You are not a participant in this conversation.");

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            $"convo:{conversationId}");
    }

    public Task LeaveConversation(int conversationId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"convo:{conversationId}");

    private static bool TryUserId(ClaimsPrincipal? user, out int id) =>
        int.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out id);
}
