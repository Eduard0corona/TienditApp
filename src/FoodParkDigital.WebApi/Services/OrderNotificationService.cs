using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace FoodParkDigital.WebApi.Services;

public class OrderNotificationService : IOrderNotificationService
{
    private readonly IHubContext<OrderHub> _hubContext;

    public OrderNotificationService(IHubContext<OrderHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyOrderCreated(Guid tenantId, Guid orderId, string orderNumber, BusinessType businessType, decimal total)
    {
        var payload = new { OrderId = orderId, OrderNumber = orderNumber, BusinessType = businessType.ToString(), Total = total };

        await _hubContext.Clients
            .Group($"tenant_{tenantId}")
            .SendAsync("OrderCreated", payload);

        if (businessType == BusinessType.Food)
        {
            await _hubContext.Clients
                .Group($"kitchen_{tenantId}")
                .SendAsync("OrderCreated", payload);
        }
    }

    public async Task NotifyOrderStatusChanged(Guid tenantId, Guid orderId, OrderStatus previousStatus, OrderStatus newStatus)
    {
        var payload = new { OrderId = orderId, PreviousStatus = previousStatus.ToString(), NewStatus = newStatus.ToString() };

        await _hubContext.Clients
            .Group($"tenant_{tenantId}")
            .SendAsync("OrderStatusChanged", payload);

        await _hubContext.Clients
            .Group($"kitchen_{tenantId}")
            .SendAsync("OrderStatusChanged", payload);
    }

    public async Task NotifyOrderCancelled(Guid tenantId, Guid orderId, OrderStatus previousStatus, string? reason)
    {
        var payload = new { OrderId = orderId, PreviousStatus = previousStatus.ToString(), Reason = reason };

        await _hubContext.Clients
            .Group($"tenant_{tenantId}")
            .SendAsync("OrderCancelled", payload);

        await _hubContext.Clients
            .Group($"kitchen_{tenantId}")
            .SendAsync("OrderCancelled", payload);
    }
}
