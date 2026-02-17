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
    Other = 99
}
