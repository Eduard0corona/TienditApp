using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FoodParkDigital.WebApi.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly ITenantAccessService _tenantAccessService;
    private readonly HubConnections _connections;

    public ChatHub(ITenantAccessService tenantAccessService, HubConnections connections)
    {
        _tenantAccessService = tenantAccessService;
        _connections = connections;
    }

    public override async Task OnConnectedAsync()
    {
        // Chat events carry customer messages: same requirement as /api/v1/whatsapp/chat.
        var access = await HubTenantAccess.ResolveAsync(Context.User, _tenantAccessService, Context.ConnectionAborted);
        if (access is null || !access.HasAnyPermission(TenantPermission.ManageWhatsApp))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{access.TenantId}");
        // Checked again while it lives: it closes when its session ends or it loses access to the chat.
        _connections.Add(Context, access => access.HasAnyPermission(TenantPermission.ManageWhatsApp));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
