namespace FoodParkDigital.Domain.Purchasing.Enums;

public enum PurchaseOrderStatus
{
    Draft = 0,
    Sent = 1,
    PartiallyReceived = 2,
    Received = 3,
    /// <summary>Part arrived and the rest will not: closed with what came in, the missing part kept on record.</summary>
    Closed = 4,
    Cancelled = 99
}
