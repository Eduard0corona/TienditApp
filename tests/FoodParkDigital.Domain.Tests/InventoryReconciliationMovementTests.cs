using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>A reconciliation of the history (Fase 27C) records a gap and leaves the stock as it is.</summary>
public class InventoryReconciliationMovementTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReconciliationLine_ShouldKeepTheStockAndCarryNoCost()
    {
        var movement = Reconciliation(InventoryMovementType.AdjustmentIncrease);
        var supply = Guid.NewGuid();
        var location = Guid.NewGuid();

        var line = movement.AddReconciliationLine(Tenant, supply, location, 2m, 28m, "Existencia 28; el historial sumaba 26");

        Assert.Equal((supply, location, 2m, 28m, 28m, (decimal?)null, (decimal?)null, "Existencia 28; el historial sumaba 26"),
            (line.SupplyItemId, line.InventoryLocationId, line.Quantity, line.QuantityBefore, line.QuantityAfter, line.UnitCost,
                line.TotalCost, line.Notes));
        Assert.Equal(0m, movement.TotalAmount);
    }

    [Theory]
    [InlineData(InventoryMovementType.Entry)]
    [InlineData(InventoryMovementType.Exit)]
    [InlineData(InventoryMovementType.TransferIn)]
    [InlineData(InventoryMovementType.TransferOut)]
    [InlineData(InventoryMovementType.ReturnFromProduction)]
    [InlineData(InventoryMovementType.ReturnToSupplier)]
    public void Reconciliation_ShouldOnlyBeAnAdjustment(InventoryMovementType type)
    {
        var exception = Assert.Throws<DomainException>(() => Reconciliation(type));

        Assert.Equal("Un cuadre del historial es un ajuste: suma o resta al historial.", exception.Message);
    }

    [Fact]
    public void ReconciliationLine_ShouldOnlyGoInAReconciliation()
    {
        var movement = new InventoryMovement(
            Tenant, "INV-20261021-0001", InventoryMovementType.AdjustmentIncrease, InventoryMovementReason.ManualAdjustment, Now);

        var exception = Assert.Throws<DomainException>(() => movement.AddReconciliationLine(Tenant, Guid.NewGuid(), Guid.NewGuid(), 2m, 28m));

        Assert.Equal("Solo un cuadre del historial anota sin mover la existencia.", exception.Message);
        Assert.Empty(movement.Lines);
    }

    private static InventoryMovement Reconciliation(InventoryMovementType type)
        => new(Tenant, "INV-20261021-0001", type, InventoryMovementReason.LedgerReconciliation, Now);
}
