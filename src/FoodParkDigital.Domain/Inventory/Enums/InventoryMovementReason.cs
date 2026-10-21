namespace FoodParkDigital.Domain.Inventory.Enums;

public enum InventoryMovementReason
{
    Purchase = 1,
    Sale = 2,
    Production = 3,
    Consumption = 4,
    Waste = 5,
    Damaged = 6,
    InventoryCount = 7,
    InitialLoad = 8,
    Return = 9,
    ManualAdjustment = 10,
    /// <summary>The two movements of a transfer between locations (Fase 27B, ADR 0038); manual movements do not take it.</summary>
    Transfer = 11,
    /// <summary>
    /// What the history of a stock was missing to add up to it (Fase 27C, ADR 0039): it records the gap and does not move
    /// the stock. Manual movements do not take it.
    /// </summary>
    LedgerReconciliation = 12,
    Other = 99
}
