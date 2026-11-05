using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FoodParkDigital.WebApi.Hubs;

/// <summary>
/// The requests online that arrive (Fase 29N, ADR 0068), for whoever answers them: the agenda's permission, as
/// /api/v1/appointments asks, in a business with the module of appointments. Each connection joins its business's group
/// only.
/// </summary>
[Authorize]
public class AppointmentHub : Hub
{
    private readonly ITenantAccessService _tenantAccessService;
    private readonly HubConnections _connections;

    public AppointmentHub(ITenantAccessService tenantAccessService, HubConnections connections)
    {
        _tenantAccessService = tenantAccessService;
        _connections = connections;
    }

    public static string GroupOf(Guid tenantId) => $"appointments_{tenantId}";

    public override async Task OnConnectedAsync()
    {
        var access = await HubTenantAccess.ResolveAsync(Context.User, _tenantAccessService, Context.ConnectionAborted);
        if (access is null
            || !access.HasAnyPermission(TenantPermission.ManageAppointments)
            || !await _tenantAccessService.HasCapabilityAsync(access.TenantId, TenantCapability.Appointments, Context.ConnectionAborted))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(access.TenantId));
        // Checked again while it lives: it closes when its session ends or it loses the agenda. A business that turns the
        // module off keeps the connection, but gets no requests online without it.
        _connections.Add(Context, access => access.HasAnyPermission(TenantPermission.ManageAppointments));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
