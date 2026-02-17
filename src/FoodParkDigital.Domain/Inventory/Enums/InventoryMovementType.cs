namespace FoodParkDigital.Domain.Inventory.Enums;

public enum InventoryMovementType
{
    Entry = 1,
    Exit = 2,
    AdjustmentIncrease = 3,
    AdjustmentDecrease = 4,
    TransferIn = 5,
    TransferOut = 6,
    ReturnFromProduction = 7,
    ReturnToSupplier = 8
}
