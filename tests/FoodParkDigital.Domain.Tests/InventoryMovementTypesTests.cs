using FoodParkDigital.Domain.Inventory.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// The ledger moves each stock with this sign and the review of the history (Fase 27C) adds the history up with it: a
/// type that changed sides would make every stock it touched look like it does not add up.
/// </summary>
public class InventoryMovementTypesTests
{
    [Fact]
    public void EveryType_ShouldHaveItsSign()
    {
        Assert.Equal(
            new[]
            {
                (InventoryMovementType.Entry, 2m),
                (InventoryMovementType.Exit, -2m),
                (InventoryMovementType.AdjustmentIncrease, 2m),
                (InventoryMovementType.AdjustmentDecrease, -2m),
                (InventoryMovementType.TransferIn, 2m),
                (InventoryMovementType.TransferOut, -2m),
                (InventoryMovementType.ReturnFromProduction, 2m),
                (InventoryMovementType.ReturnToSupplier, -2m)
            },
            Enum.GetValues<InventoryMovementType>().Select(type => (type, InventoryMovementTypes.Signed(type, 2m))));
    }
}
