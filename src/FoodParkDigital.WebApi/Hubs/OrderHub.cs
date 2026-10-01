using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FoodParkDigital.WebApi.Hubs;

[Authorize]
public class OrderHub : Hub
{
    // Order events feed the order list, the service and kitchen boards and the dashboard.
    private static readonly TenantPermission[] OrderEventPermissions =
    [
        TenantPermission.ManageOrders,
        TenantPermission.ManageServiceBoard,
        TenantPermission.ViewReports
    ];

    private readonly ITenantAccessService _tenantAccessService;

    public OrderHub(ITenantAccessService tenantAccessService)
    {
        _tenantAccessService = tenantAccessService;
    }

    public override async Task OnConnectedAsync()
    {
        var access = await HubTenantAccess.ResolveAsync(Context.User, _tenantAccessService, Context.ConnectionAborted);
        if (access is null || !access.HasAnyPermission(OrderEventPermissions))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant_{access.TenantId}");
        await base.OnConnectedAsync();
    }

    public async Task JoinKitchenGroup()
    {
        var access = await HubTenantAccess.ResolveAsync(Context.User, _tenantAccessService, Context.ConnectionAborted);
        if (access is not null && (access.Role is "KITCHEN" || access.IsTenantAdmin || access.IsSuperAdmin))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"kitchen_{access.TenantId}");
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
