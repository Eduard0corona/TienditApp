using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FoodParkDigital.WebApi.Hubs;

[Authorize]
public class OrderHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value;
        if (tenantId is not null)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant_{tenantId}");
        }
        await base.OnConnectedAsync();
    }

    public async Task JoinKitchenGroup()
    {
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value;
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;

        if (tenantId is not null && (role == "KITCHEN" || role == "TENANT_ADMIN" || role == "SUPERADMIN"))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"kitchen_{tenantId}");
        }
    }

    public async Task LeaveKitchenGroup()
    {
        var tenantId = Context.User?.FindFirst("tenant_id")?.Value;
        if (tenantId is not null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"kitchen_{tenantId}");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
