using System.Security.Claims;


using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CDM_OneServe_API.Hubs;

/// <summary>
/// Real-time channel for Guidance. Every authenticated user joins the
/// "user:{id}" group so the server can push appointment and counseling
/// notifications to that specific user the moment they happen.
/// </summary>
[Authorize]
public class GuidanceHub : Hub
{
    /// <summary>
    /// Live connection count for the system-monitoring page. In-process and
    /// approximate on purpose: it is telemetry, never an authorisation input.
    /// </summary>
    private static int _activeConnections;

    public static int ActiveConnections => Volatile.Read(ref _activeConnections);

    public override async Task OnConnectedAsync()
    {
        if (TryUserId(Context.User, out var id))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{id}");

        Interlocked.Increment(ref _activeConnections);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Interlocked.Decrement(ref _activeConnections);

        await base.OnDisconnectedAsync(exception);
    }

    private static bool TryUserId(ClaimsPrincipal? user, out int id) =>
        int.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out id);
}
