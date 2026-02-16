using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Orders.StateMachines;

public class ServiceOrderStateMachine : IOrderStateMachine
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Transitions = new()
    {
        { OrderStatus.Pending, [OrderStatus.Accepted, OrderStatus.Confirmed, OrderStatus.InProgress, OrderStatus.Completed, OrderStatus.Cancelled] },
        { OrderStatus.Confirmed, [OrderStatus.Accepted, OrderStatus.InProgress, OrderStatus.Completed, OrderStatus.Cancelled] },
        { OrderStatus.Accepted, [OrderStatus.Confirmed, OrderStatus.InProgress, OrderStatus.Completed, OrderStatus.Cancelled] },
        { OrderStatus.InProgress, [OrderStatus.Completed, OrderStatus.Cancelled] },
        { OrderStatus.Completed, [] },
        { OrderStatus.Cancelled, [] }
    };

    public IReadOnlyList<OrderStatus> GetAllowedTransitions(OrderStatus currentStatus)
        => Transitions.TryGetValue(currentStatus, out var allowed)
            ? allowed
            : Array.Empty<OrderStatus>();

    public void ValidateTransition(OrderStatus from, OrderStatus to)
    {
        if (!Transitions.TryGetValue(from, out var allowed) || !allowed.Contains(to))
            throw new InvalidOrderStateTransitionException(from, to, BusinessType.Service);
    }

    public bool CanCancel(OrderStatus currentStatus)
        => currentStatus is OrderStatus.Pending or OrderStatus.Accepted or OrderStatus.Confirmed or OrderStatus.InProgress;

    public OrderStatus GetInitialStatus() => OrderStatus.Pending;
}
