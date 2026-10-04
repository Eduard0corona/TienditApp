using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Common.Interfaces;

public interface IOrderNotificationService
{
    Task NotifyOrderCreated(Guid tenantId, Guid orderId, string orderNumber, BusinessType businessType, decimal total);
    Task NotifyOrderStatusChanged(Guid tenantId, Guid orderId, OrderStatus previousStatus, OrderStatus newStatus);
    Task NotifyOrderCancelled(Guid tenantId, Guid orderId, OrderStatus previousStatus, string? reason);
    /// <summary>A round of an open tab was sent or moved in the kitchen (kitchen board and dining room refresh).</summary>
    Task NotifyKitchenTicketsChanged(Guid tenantId, Guid orderId, int round);
    Task NotifyServiceBoardPieceUpdated(
        Guid tenantId,
        Guid orderId,
        Guid pieceId,
        ServiceOrderPieceStatus status,
        Guid? assignedToUserId);
}
