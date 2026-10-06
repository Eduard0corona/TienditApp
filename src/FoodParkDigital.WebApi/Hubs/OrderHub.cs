using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FoodParkDigital.WebApi.Hubs;

[Authorize]
public class OrderHub : Hub
{
    // Order events feed the order list, the service and kitchen boards and the dashboard. Every
    // connection joins its tenant group only: the kitchen board needs ManageServiceBoard, so it already
    // receives the same events there (a separate kitchen group duplicated them and checked roles).
    private static readonly TenantPermission[] OrderEventPermissions =
    [
        TenantPermission.ManageOrders,
        TenantPermission.ManageServiceBoard,
        TenantPermission.ViewReports
    ];

    private readonly ITenantAccessService _tenantAccessService;
    private readonly HubConnections _connections;

    public OrderHub(ITenantAccessService tenantAccessService, HubConnections connections)
    {
        _tenantAccessService = tenantAccessService;
        _connections = connections;
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
        // Checked again while it lives: it closes when its session ends or it loses these permissions.
        _connections.Add(Context, access => access.HasAnyPermission(OrderEventPermissions));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
