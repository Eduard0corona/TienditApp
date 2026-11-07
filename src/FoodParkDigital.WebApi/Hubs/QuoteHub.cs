using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FoodParkDigital.WebApi.Hubs;

/// <summary>
/// The answers customers give to the quotes from their link (Fase 29R, ADR 0072), for whoever works the quotes: the orders'
/// permission, as /api/v1/quotes asks, in a business with services or appointments. Each connection joins its business's
/// group only.
/// </summary>
[Authorize]
public class QuoteHub : Hub
{
    /// <summary>The modules that have quotes, as their API asks.</summary>
    public const TenantCapability Modules = TenantCapability.Service | TenantCapability.Appointments;

    private readonly ITenantAccessService _tenantAccessService;
    private readonly HubConnections _connections;

    public QuoteHub(ITenantAccessService tenantAccessService, HubConnections connections)
    {
        _tenantAccessService = tenantAccessService;
        _connections = connections;
    }

    public static string GroupOf(Guid tenantId) => $"quotes_{tenantId}";

    public override async Task OnConnectedAsync()
    {
        var access = await HubTenantAccess.ResolveAsync(Context.User, _tenantAccessService, Context.ConnectionAborted);
        if (access is null
            || !access.HasAnyPermission(TenantPermission.ManageOrders)
            || !await _tenantAccessService.HasAnyCapabilityAsync(access.TenantId, Modules, Context.ConnectionAborted))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(access.TenantId));
        // Checked again while it lives: it closes when its session ends or it loses the orders' permission.
        _connections.Add(Context, access => access.HasAnyPermission(TenantPermission.ManageOrders));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
