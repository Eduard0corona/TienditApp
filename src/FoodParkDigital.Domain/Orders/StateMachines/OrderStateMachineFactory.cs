using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Orders.StateMachines;

public static class OrderStateMachineFactory
{
    private static readonly FoodOrderStateMachine Food = new();
    private static readonly RetailOrderStateMachine Retail = new();
    private static readonly ServiceOrderStateMachine Service = new();

    public static IOrderStateMachine Create(BusinessType businessType) => businessType switch
    {
        BusinessType.Food => Food,
        BusinessType.Retail => Retail,
        BusinessType.Service => Service,
        _ => throw new ArgumentOutOfRangeException(nameof(businessType), businessType, "Unsupported business type.")
    };
}
