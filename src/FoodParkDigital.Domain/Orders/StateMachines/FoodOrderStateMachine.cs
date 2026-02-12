using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Orders.StateMachines;

public class FoodOrderStateMachine : IOrderStateMachine
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Transitions = new()
    {
        { OrderStatus.Pending, [OrderStatus.InPreparation, OrderStatus.Cancelled] },
        { OrderStatus.InPreparation, [OrderStatus.Ready, OrderStatus.Cancelled] },
        { OrderStatus.Ready, [OrderStatus.Delivered] },
        { OrderStatus.Delivered, [] },
        { OrderStatus.Cancelled, [] }
    };

    public IReadOnlyList<OrderStatus> GetAllowedTransitions(OrderStatus currentStatus)
        => Transitions.TryGetValue(currentStatus, out var allowed)
            ? allowed
            : Array.Empty<OrderStatus>();

    public void ValidateTransition(OrderStatus from, OrderStatus to)
    {
        if (!Transitions.TryGetValue(from, out var allowed) || !allowed.Contains(to))
            throw new InvalidOrderStateTransitionException(from, to, BusinessType.Food);
    }

    public bool CanCancel(OrderStatus currentStatus)
        => currentStatus is OrderStatus.Pending or OrderStatus.InPreparation;

    public OrderStatus GetInitialStatus() => OrderStatus.Pending;
}
