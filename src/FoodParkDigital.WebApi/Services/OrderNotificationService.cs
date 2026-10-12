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

    public async Task NotifyOrderCreated(
        Guid tenantId,
        Guid orderId,
        string orderNumber,
        BusinessType businessType,
        decimal total,
        OrderStatus status,
        OrderChannel channel,
        string? externalOrderNumber)
    {
        // The folio of a platform's app identifies the order, not the customer: nothing personal goes out.
        var payload = new
        {
            OrderId = orderId,
            OrderNumber = orderNumber,
            BusinessType = businessType.ToString(),
            Total = total,
            Status = status.ToString(),
            Channel = channel.ToString(),
            ExternalOrderNumber = externalOrderNumber
        };

        await _hubContext.Clients
            .Group($"tenant_{tenantId}")
            .SendAsync("OrderCreated", payload);
    }

    public async Task NotifyOrderStatusChanged(Guid tenantId, Guid orderId, OrderStatus previousStatus, OrderStatus newStatus)
    {
        var payload = new { OrderId = orderId, PreviousStatus = previousStatus.ToString(), NewStatus = newStatus.ToString() };

        await _hubContext.Clients
            .Group($"tenant_{tenantId}")
            .SendAsync("OrderStatusChanged", payload);
    }

    public async Task NotifyOrderCancelled(Guid tenantId, Guid orderId, OrderStatus previousStatus, string? reason)
    {
        var payload = new { OrderId = orderId, PreviousStatus = previousStatus.ToString(), Reason = reason };

        await _hubContext.Clients
            .Group($"tenant_{tenantId}")
            .SendAsync("OrderCancelled", payload);
    }

    public async Task NotifyKitchenTicketsChanged(Guid tenantId, Guid orderId, int round)
    {
        var payload = new { OrderId = orderId, Round = round };

        await _hubContext.Clients
            .Group($"tenant_{tenantId}")
            .SendAsync("KitchenTicketsChanged", payload);
    }

    public async Task NotifyServiceBoardPieceUpdated(
        Guid tenantId,
        Guid orderId,
        Guid pieceId,
        ServiceOrderPieceStatus status,
        Guid? assignedToUserId)
    {
        var payload = new
        {
            OrderId = orderId,
            PieceId = pieceId,
            Status = status.ToString(),
            AssignedToUserId = assignedToUserId
        };

        await _hubContext.Clients
            .Group($"tenant_{tenantId}")
            .SendAsync("ServiceBoardPieceUpdated", payload);
    }
}
