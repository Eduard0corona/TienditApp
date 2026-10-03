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

    // Cancellation follows the transition table, the single source of truth for every path.
    public bool CanCancel(OrderStatus currentStatus)
        => GetAllowedTransitions(currentStatus).Contains(OrderStatus.Cancelled);

    public OrderStatus GetInitialStatus() => OrderStatus.Pending;
}
