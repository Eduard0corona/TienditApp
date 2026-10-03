using FoodParkDigital.Domain.Orders.StateMachines;

namespace FoodParkDigital.Application.Orders.DTOs;

public static class OrderDtoStateExtensions
{
    public static void ApplyStateMetadata(this OrderDto order)
    {
        var stateMachine = OrderStateMachineFactory.Create(order.BusinessType, order.OrderType);
        order.AllowedTransitions = stateMachine.GetAllowedTransitions(order.Status).ToList();
        // Mirrors Order.Cancel: orders with registered payments cannot be cancelled until refunds exist.
        order.CanCancel = stateMachine.CanCancel(order.Status) && order.PaidAmount <= 0;
    }

    public static void ApplyStateMetadata(this IEnumerable<OrderDto> orders)
    {
        foreach (var order in orders)
            order.ApplyStateMetadata();
    }
}
