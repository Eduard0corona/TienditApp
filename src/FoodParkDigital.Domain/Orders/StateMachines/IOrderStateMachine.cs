using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Domain.Orders.StateMachines;

public interface IOrderStateMachine
{
    IReadOnlyList<OrderStatus> GetAllowedTransitions(OrderStatus currentStatus);
    void ValidateTransition(OrderStatus from, OrderStatus to);
    bool CanCancel(OrderStatus currentStatus);
    OrderStatus GetInitialStatus();
}
