using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Purchasing.Entities;

public class PurchaseOrderLine : AuditableEntity, ITenantScoped
{
    private PurchaseOrderLine() { }

    public PurchaseOrderLine(
        Guid tenantId,
        Guid purchaseOrderId,
        Guid supplyItemId,
        Guid inventoryLocationId,
        decimal quantity,
        decimal unitCost,
        decimal taxPercent = 0,
        string? notes = null)
    {
        if (quantity <= 0)
            throw new DomainException("La cantidad debe ser mayor a cero.");

        if (unitCost < 0)
            throw new DomainException("El costo unitario no puede ser negativo.");

        if (taxPercent < 0 || taxPercent > 100)
            throw new DomainException("El impuesto debe estar entre 0 y 100.");

        TenantId = tenantId;
        PurchaseOrderId = purchaseOrderId;
        SupplyItemId = supplyItemId;
        InventoryLocationId = inventoryLocationId;
        Quantity = quantity;
        ReceivedQuantity = 0;
        UnitCost = unitCost;
        TaxPercent = taxPercent;
        Notes = notes?.Trim();

        RecalculateTotals();
    }

    public Guid TenantId { get; private set; }
    public Guid PurchaseOrderId { get; private set; }
    public Guid SupplyItemId { get; private set; }
    public Guid InventoryLocationId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public decimal PendingQuantity => Quantity - ReceivedQuantity;
    public decimal UnitCost { get; private set; }
    public decimal TaxPercent { get; private set; }
    public decimal SubTotal { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal Total { get; private set; }
    public string? Notes { get; private set; }

    public PurchaseOrder PurchaseOrder { get; private set; } = null!;

    public void Receive(decimal quantity)
    {
        if (quantity <= 0)
            throw new DomainException("La cantidad recibida debe ser mayor a cero.");
        if (quantity > PendingQuantity)
            throw new DomainException("No puedes recibir más de la cantidad pendiente.");

        ReceivedQuantity += quantity;
    }

    private void RecalculateTotals()
    {
        SubTotal = Math.Round(Quantity * UnitCost, 4);
        TaxAmount = Math.Round(SubTotal * (TaxPercent / 100m), 4);
        Total = SubTotal + TaxAmount;
    }
}
