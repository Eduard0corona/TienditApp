using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Inventory.Entities;

public class ProductSupplyRequirement : AuditableEntity, ITenantScoped
{
    private ProductSupplyRequirement() { }

    public ProductSupplyRequirement(
        Guid tenantId,
        Guid productId,
        Guid supplyItemId,
        decimal quantity,
        Guid? inventoryLocationId = null,
        decimal wastePercent = 0,
        string? notes = null)
    {
        if (quantity <= 0)
            throw new DomainException("La cantidad de insumo por producto debe ser mayor a cero.");

        if (wastePercent < 0 || wastePercent > 100)
            throw new DomainException("El porcentaje de merma debe estar entre 0 y 100.");

        TenantId = tenantId;
        ProductId = productId;
        SupplyItemId = supplyItemId;
        InventoryLocationId = inventoryLocationId;
        Quantity = quantity;
        WastePercent = wastePercent;
        Notes = notes;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid SupplyItemId { get; private set; }
    public Guid? InventoryLocationId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal WastePercent { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }

    public SupplyItem SupplyItem { get; private set; } = null!;

    public void Update(decimal quantity, Guid? inventoryLocationId, decimal wastePercent, string? notes)
    {
        if (quantity <= 0)
            throw new DomainException("La cantidad de insumo por producto debe ser mayor a cero.");

        if (wastePercent < 0 || wastePercent > 100)
            throw new DomainException("El porcentaje de merma debe estar entre 0 y 100.");

        Quantity = quantity;
        InventoryLocationId = inventoryLocationId;
        WastePercent = wastePercent;
        Notes = notes;
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}
