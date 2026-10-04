using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Orders;

/// <summary>Applies the business policy that closes counter sales once they are fully paid.</summary>
public static class CounterSaleClosing
{
    /// <summary>Food sold by a business with the kitchen screen is handed over once the kitchen marks it ready.</summary>
    public static bool WaitsForKitchen(Order order, TenantCapability capabilities)
        => order.BusinessType == BusinessType.Food
           && (capabilities & TenantCapability.Kitchen) == TenantCapability.Kitchen;

    /// <returns>True when the sale was closed.</returns>
    public static bool TryClose(
        Order order,
        TenantSalesSettings settings,
        TenantCapability capabilities,
        string? performedBy)
    {
        if (!settings.AutoCloseCounterSales)
            return false;

        var stateMachine = OrderStateMachineFactory.Create(order.BusinessType, order.OrderType);
        return order.CloseIfPaidCounterSale(stateMachine, WaitsForKitchen(order, capabilities), performedBy);
    }
}
