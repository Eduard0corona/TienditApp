namespace FoodParkDigital.Domain.Inventory.Enums;

/// <summary>
/// Which way each movement type moves the stock of its location: the types that enter add their quantity, every other
/// takes it away. The ledger applies it to the stock and the review of the history (Fase 27C, ADR 0039) adds the
/// history up with it, so both read the same sign.
/// </summary>
public static class InventoryMovementTypes
{
    /// <summary>The types that add to the stock of their location.</summary>
    public static readonly IReadOnlyList<InventoryMovementType> Inbound =
    [
        InventoryMovementType.Entry,
        InventoryMovementType.AdjustmentIncrease,
        InventoryMovementType.TransferIn,
        InventoryMovementType.ReturnFromProduction
    ];

    public static bool IsInbound(InventoryMovementType type) => Inbound.Contains(type);

    /// <summary>How much a line of <paramref name="quantity"/> (always positive) changes the stock of its location.</summary>
    public static decimal Signed(InventoryMovementType type, decimal quantity) => IsInbound(type) ? quantity : -quantity;
}
