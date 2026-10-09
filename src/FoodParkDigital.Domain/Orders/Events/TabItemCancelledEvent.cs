using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Orders.Events;

/// <summary>
/// Units of a dish of an open tab were cancelled after going to the kitchen: the board refreshes and the activity log
/// records it.
/// </summary>
public sealed record TabItemCancelledEvent(
    Guid OrderId,
    Guid TenantId,
    Guid CancellationId,
    Guid OrderItemId,
    int Round,
    string ProductName,
    decimal Quantity,
    decimal Amount,
    string Reason,
    bool ReturnedToStock) : DomainEvent;
