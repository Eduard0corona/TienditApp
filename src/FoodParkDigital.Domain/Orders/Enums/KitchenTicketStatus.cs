namespace FoodParkDigital.Domain.Orders.Enums;

/// <summary>Where a round of an open tab is in the kitchen. It only moves forward.</summary>
public enum KitchenTicketStatus
{
    Pending = 0,
    Preparing = 1,
    Ready = 2,
    Served = 3
}
