using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Inventory.Entities;

public class InventoryStock : AuditableEntity, ITenantScoped
{
    private InventoryStock() { }

    public InventoryStock(Guid tenantId, Guid supplyItemId, Guid inventoryLocationId)
    {
        TenantId = tenantId;
        SupplyItemId = supplyItemId;
        InventoryLocationId = inventoryLocationId;
    }

    public Guid TenantId { get; private set; }
    public Guid SupplyItemId { get; private set; }
    public Guid InventoryLocationId { get; private set; }
    public decimal OnHandQuantity { get; private set; }
    public decimal ReservedQuantity { get; private set; }
    public decimal LastCost { get; private set; }
    public DateTimeOffset? LastMovementAt { get; private set; }

    public decimal AvailableQuantity => OnHandQuantity - ReservedQuantity;

    public SupplyItem SupplyItem { get; private set; } = null!;
    public InventoryLocation InventoryLocation { get; private set; } = null!;

    public void ApplyMovement(decimal quantityDelta, decimal? unitCost, DateTimeOffset movementDate, bool allowNegative = false)
    {
        var newOnHand = OnHandQuantity + quantityDelta;
        if (!allowNegative && newOnHand < 0)
            throw new DomainException("No hay stock suficiente para realizar la salida.");

        OnHandQuantity = newOnHand;
        LastMovementAt = movementDate;

        if (unitCost.HasValue)
            LastCost = unitCost.Value;
    }

    public void SetReservedQuantity(decimal reservedQuantity)
    {
        if (reservedQuantity < 0)
            throw new DomainException("La cantidad reservada no puede ser negativa.");
        if (reservedQuantity > OnHandQuantity)
            throw new DomainException("La cantidad reservada no puede ser mayor al stock disponible.");

        ReservedQuantity = reservedQuantity;
    }
}
