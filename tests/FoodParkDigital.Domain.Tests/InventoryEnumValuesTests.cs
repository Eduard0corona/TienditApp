using FoodParkDigital.Domain.Inventory.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Movement types and reasons (Transfer since Fase 27B, LedgerReconciliation since Fase 27C), and the states of a physical
/// count, are stored as integers and travel to the client as numbers. Renumbering them, even to match a client,
/// reinterprets every movement already saved, so the values are fixed.
/// </summary>
public class InventoryEnumValuesTests
{
    [Fact]
    public void MovementTypes_ShouldKeepTheirPersistedValues()
    {
        Assert.Equal(
            new[]
            {
                ("Entry", 1),
                ("Exit", 2),
                ("AdjustmentIncrease", 3),
                ("AdjustmentDecrease", 4),
                ("TransferIn", 5),
                ("TransferOut", 6),
                ("ReturnFromProduction", 7),
                ("ReturnToSupplier", 8)
            },
            Enum.GetValues<InventoryMovementType>().Select(x => (x.ToString(), (int)x)));
    }

    [Fact]
    public void MovementReasons_ShouldKeepTheirPersistedValues()
    {
        Assert.Equal(
            new[]
            {
                ("Purchase", 1),
                ("Sale", 2),
                ("Production", 3),
                ("Consumption", 4),
                ("Waste", 5),
                ("Damaged", 6),
                ("InventoryCount", 7),
                ("InitialLoad", 8),
                ("Return", 9),
                ("ManualAdjustment", 10),
                ("Transfer", 11),
                ("LedgerReconciliation", 12),
                ("Other", 99)
            },
            Enum.GetValues<InventoryMovementReason>().Select(x => (x.ToString(), (int)x)));
    }

    [Fact]
    public void CountStatuses_ShouldKeepTheirPersistedValues()
    {
        Assert.Equal(
            new[] { ("InProgress", 1), ("Posted", 2), ("Cancelled", 3) },
            Enum.GetValues<InventoryCountStatus>().Select(x => (x.ToString(), (int)x)));
    }
}
