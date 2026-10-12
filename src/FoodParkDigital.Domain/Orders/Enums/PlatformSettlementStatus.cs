namespace FoodParkDigital.Domain.Orders.Enums;

/// <summary>Where a settlement of a delivery platform's deposit stands (Reportes › Liquidaciones).</summary>
public enum PlatformSettlementStatus
{
    /// <summary>Being reconciled: its deposit, orders and adjustments still change.</summary>
    Open = 0,
    /// <summary>Every peso of the deposit explained: it no longer changes, and its orders are settled.</summary>
    Closed = 1,
    /// <summary>Voided with a reason: kept as history, and its orders are pending again.</summary>
    Voided = 99
}
