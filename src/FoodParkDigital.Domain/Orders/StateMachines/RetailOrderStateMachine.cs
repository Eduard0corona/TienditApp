using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Orders.StateMachines;

public class RetailOrderStateMachine : IOrderStateMachine
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Transitions = new()
    {
        { OrderStatus.Pending, [OrderStatus.Confirmed, OrderStatus.Cancelled] },
        { OrderStatus.Confirmed, [OrderStatus.Delivered] },
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
            throw new InvalidOrderStateTransitionException(from, to, BusinessType.Retail);
    }

    public bool CanCancel(OrderStatus currentStatus)
        => currentStatus is OrderStatus.Pending;

    public OrderStatus GetInitialStatus() => OrderStatus.Pending;
}
