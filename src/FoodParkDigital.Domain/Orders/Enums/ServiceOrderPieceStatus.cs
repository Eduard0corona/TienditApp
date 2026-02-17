namespace FoodParkDigital.Domain.Orders.Enums;

public enum ServiceOrderPieceStatus
{
    Received = 0,
    Inspection = 1,
    Washing = 2,
    Drying = 3,
    Detailing = 4,
    Ready = 5,
    Delivered = 6,
    Cancelled = 99
}
