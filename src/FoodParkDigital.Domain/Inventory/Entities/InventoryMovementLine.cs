using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Inventory.Entities;

public class InventoryMovementLine : AuditableEntity, ITenantScoped
{
    private InventoryMovementLine() { }

    public InventoryMovementLine(
        Guid tenantId,
        Guid movementId,
        Guid supplyItemId,
        Guid inventoryLocationId,
        decimal quantity,
        decimal? unitCost = null,
        string? lotNumber = null,
        DateTimeOffset? expirationDate = null,
        string? notes = null)
    {
        if (quantity <= 0)
            throw new DomainException("La cantidad del movimiento debe ser mayor a cero.");

        TenantId = tenantId;
        MovementId = movementId;
        SupplyItemId = supplyItemId;
        InventoryLocationId = inventoryLocationId;
        Quantity = quantity;
        UnitCost = unitCost;
        TotalCost = unitCost.HasValue ? unitCost.Value * quantity : null;
        LotNumber = lotNumber;
        ExpirationDate = expirationDate;
        Notes = notes;
    }

    public Guid TenantId { get; private set; }
    public Guid MovementId { get; private set; }
    public Guid SupplyItemId { get; private set; }
    public Guid InventoryLocationId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal? UnitCost { get; private set; }
    public decimal? TotalCost { get; private set; }
    public decimal QuantityBefore { get; private set; }
    public decimal QuantityAfter { get; private set; }
    public string? LotNumber { get; private set; }
    public DateTimeOffset? ExpirationDate { get; private set; }
    public string? Notes { get; private set; }

    public InventoryMovement Movement { get; private set; } = null!;
    public SupplyItem SupplyItem { get; private set; } = null!;
    public InventoryLocation InventoryLocation { get; private set; } = null!;

    public void SetBalances(decimal quantityBefore, decimal quantityAfter)
    {
        QuantityBefore = quantityBefore;
        QuantityAfter = quantityAfter;
    }
}
