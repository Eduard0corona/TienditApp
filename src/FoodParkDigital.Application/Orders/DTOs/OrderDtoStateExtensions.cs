using FoodParkDigital.Domain.Orders.StateMachines;

namespace FoodParkDigital.Application.Orders.DTOs;

public static class OrderDtoStateExtensions
{
    public static void ApplyStateMetadata(this OrderDto order)
    {
        var stateMachine = OrderStateMachineFactory.Create(order.BusinessType, order.OrderType);
        order.AllowedTransitions = stateMachine.GetAllowedTransitions(order.Status).ToList();
        order.CanCancel = stateMachine.CanCancel(order.Status);
    }

    public static void ApplyStateMetadata(this IEnumerable<OrderDto> orders)
    {
        foreach (var order in orders)
            order.ApplyStateMetadata();
    }
}
