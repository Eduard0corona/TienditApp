using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FoodParkDigital.WebApi.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly ITenantAccessService _tenantAccessService;

    public ChatHub(ITenantAccessService tenantAccessService)
    {
        _tenantAccessService = tenantAccessService;
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
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
