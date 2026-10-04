using FoodParkDigital.Domain.Orders.Entities;

namespace FoodParkDigital.Application.Common.Interfaces;

public interface IOrderInventoryService
{
    Task ApplyConsumptionForOrderAsync(Order order, CancellationToken cancellationToken);
    Task ApplyConsumptionForServicePieceAsync(Order order, ServiceOrderPiece piece, CancellationToken cancellationToken);
    Task ReverseConsumptionForOrderAsync(Order order, string? reason, CancellationToken cancellationToken);

    /// <summary>
    /// Re-enters the recipe supplies of the units returned with <paramref name="refund"/>, when the order
    /// consumed inventory. Idempotent per refund.
    /// </summary>
    Task ReturnItemsToInventoryAsync(Order order, OrderRefund refund, CancellationToken cancellationToken);
}
