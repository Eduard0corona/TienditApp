using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace FoodParkDigital.WebApi.Services;

/// <summary>Fase 31A (ADR 0083): «Mis pedidos» of the account hears which of its orders changed, and nothing else.</summary>
public class CustomerOrderNotificationService : ICustomerOrderNotificationService
{
    private readonly IHubContext<MyOrdersHub> _hubContext;

    public CustomerOrderNotificationService(IHubContext<MyOrdersHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyOrderChanged(Guid customerUserId, Guid orderId) =>
        _hubContext.Clients.Group(MyOrdersHub.GroupOf(customerUserId)).SendAsync("MyOrderChanged", new { OrderId = orderId });
}
