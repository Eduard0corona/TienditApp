namespace FoodParkDigital.Domain.Inventory.Enums;

/// <summary>Where a physical count is (Fase 27A, ADR 0037). Stored and sent as integers: the values are fixed.</summary>
public enum InventoryCountStatus
{
    /// <summary>Open: items are counted and added.</summary>
    InProgress = 1,
    /// <summary>Its differences moved the stock; it no longer changes.</summary>
    Posted = 2,
    /// <summary>Closed without moving anything.</summary>
    Cancelled = 3
}
