using FoodParkDigital.Domain.Orders.Entities;

namespace FoodParkDigital.Application.Common.Interfaces;

public interface IOrderInventoryService
{
    Task ApplyConsumptionForOrderAsync(Order order, CancellationToken cancellationToken);
    Task ApplyConsumptionForServicePieceAsync(Order order, ServiceOrderPiece piece, CancellationToken cancellationToken);
    Task ReverseConsumptionForOrderAsync(Order order, string? reason, CancellationToken cancellationToken);
}
