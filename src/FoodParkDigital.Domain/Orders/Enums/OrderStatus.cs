namespace FoodParkDigital.Domain.Orders.Enums;

public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    InPreparation = 2,
    Ready = 3,
    Accepted = 4,
    InProgress = 5,
    Delivered = 6,
    Completed = 7,
    Cancelled = 99
}
