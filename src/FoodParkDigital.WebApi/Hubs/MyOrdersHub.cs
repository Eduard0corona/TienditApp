using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FoodParkDigital.WebApi.Hubs;

/// <summary>
/// Fase 31A (ADR 0083): the hub of an account, not of a business. «Mis pedidos» of a signed-in customer hears that one
/// of their orders changed and reads it again through <c>/api/v1/me</c>, which checks it is theirs. Each connection
/// joins only the group of its own account, and only the id of the order travels.
/// </summary>
[Authorize]
public class MyOrdersHub : Hub
{
    private readonly HubConnections _connections;

    public MyOrdersHub(HubConnections connections)
    {
        _connections = connections;
    }

    public static string GroupOf(Guid userId) => $"account_{userId}";

    public override async Task OnConnectedAsync()
    {
        if (!Guid.TryParse(Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(userId));
        // Checked again while it lives: it closes when its session ends. It has no business access to lose.
        _connections.AddAccount(Context);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
