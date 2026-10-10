using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

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
    public override async Task OnConnectedAsync()
    {
        if (TryUserId(Context.User, out var id))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{id}");

        await base.OnConnectedAsync();
    }

    public Task JoinConversation(int conversationId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"convo:{conversationId}");

    public Task LeaveConversation(int conversationId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"convo:{conversationId}");

    private static bool TryUserId(ClaimsPrincipal? user, out int id) =>
        int.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out id);
}
