namespace FoodParkDigital.Domain.Orders.Enums;

/// <summary>
/// Why what a courier hands over is not what its orders add up to (Fase 22D): each adjustment of a settlement says one of
/// these, with a signed amount. A trip, a fee or a shortage is deducted (negative); «Otro» goes either way.
/// </summary>
public enum CourierSettlementAdjustmentKind
{
    /// <summary>The courier's charge for the trips, taken from the cash it collected.</summary>
    TripFee = 1,
    /// <summary>What the courier charges for collecting in cash.</summary>
    CollectionFee = 2,
    /// <summary>Cash the courier collected and does not hand over.</summary>
    Shortage = 3,
    /// <summary>Anything else, always with a description.</summary>
    Other = 99
}
