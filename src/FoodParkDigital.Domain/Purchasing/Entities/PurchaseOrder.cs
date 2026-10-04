using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Purchasing.Enums;

namespace FoodParkDigital.Domain.Purchasing.Entities;

public class PurchaseOrder : AuditableEntity, ITenantScoped, IAggregateRoot
{
    private readonly List<PurchaseOrderLine> _lines = new();

    private PurchaseOrder() { }

    public PurchaseOrder(
        Guid tenantId,
        Guid supplierId,
        string orderNumber,
        DateTimeOffset orderedAt,
        DateTimeOffset? expectedDeliveryAt = null,
        string? referenceNumber = null,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            throw new DomainException("El folio de compra es obligatorio.");

        TenantId = tenantId;
        SupplierId = supplierId;
        OrderNumber = orderNumber.Trim();
        OrderedAt = orderedAt;
        ExpectedDeliveryAt = expectedDeliveryAt;
        ReferenceNumber = referenceNumber?.Trim();
        Notes = notes?.Trim();
        Status = PurchaseOrderStatus.Draft;
    }

    public Guid TenantId { get; private set; }
    public Guid SupplierId { get; private set; }
    public string OrderNumber { get; private set; } = null!;
    public PurchaseOrderStatus Status { get; private set; }
    public DateTimeOffset OrderedAt { get; private set; }
    public DateTimeOffset? ExpectedDeliveryAt { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }
    public string? ReferenceNumber { get; private set; }
    public string? Notes { get; private set; }
    public decimal SubTotal { get; private set; }
    public decimal Tax { get; private set; }
    public decimal Total { get; private set; }

    public Supplier Supplier { get; private set; } = null!;
    public IReadOnlyCollection<PurchaseOrderLine> Lines => _lines.AsReadOnly();

    public PurchaseOrderLine AddLine(
        Guid supplyItemId,
        Guid inventoryLocationId,
        decimal quantity,
        decimal unitCost,
        decimal taxPercent,
        string? notes = null)
    {
        if (Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled)
            throw new DomainException("No se pueden agregar líneas a una compra cerrada.");

        if (_lines.Any(x =>
            x.SupplyItemId == supplyItemId &&
            x.InventoryLocationId == inventoryLocationId))
        {
            throw new DomainException("No puedes repetir insumo y ubicación en una misma compra.");
        }

        var line = new PurchaseOrderLine(
            tenantId: TenantId,
            purchaseOrderId: Id,
            supplyItemId: supplyItemId,
            inventoryLocationId: inventoryLocationId,
            quantity: quantity,
            unitCost: unitCost,
            taxPercent: taxPercent,
            notes: notes);

        _lines.Add(line);
        RecalculateTotals();
        return line;
    }

    public void MarkAsSent()
    {
        if (Status != PurchaseOrderStatus.Draft)
            throw new DomainException("Solo una compra en borrador puede enviarse.");
        if (_lines.Count == 0)
            throw new DomainException("Debes capturar al menos una línea para enviar la compra.");

        Status = PurchaseOrderStatus.Sent;
    }

    /// <summary>Only an order with nothing received yet: received goods already are stock and spend.</summary>
    public void Cancel()
    {
        if (Status == PurchaseOrderStatus.Cancelled)
            throw new DomainException("La compra ya está cancelada.");
        if (Status == PurchaseOrderStatus.Received)
            throw new DomainException("No se puede cancelar una compra ya recibida.");
        if (_lines.Any(x => x.ReceivedQuantity > 0))
            throw new DomainException("La compra ya tiene mercancía recibida; no se puede cancelar.");

        Status = PurchaseOrderStatus.Cancelled;
    }

    /// <param name="unitCostByLineId">The supplier's actual price per line when it differs from the ordered one.</param>
    public void RegisterReception(
        IReadOnlyDictionary<Guid, decimal> receivedByLineId,
        DateTimeOffset receivedAt,
        IReadOnlyDictionary<Guid, decimal>? unitCostByLineId = null)
    {
        if (Status == PurchaseOrderStatus.Cancelled)
            throw new DomainException("No se puede recibir una compra cancelada.");

        if (_lines.Count == 0)
            throw new DomainException("La compra no tiene líneas para recibir.");

        var anyReceived = false;
        foreach (var line in _lines)
        {
            if (!receivedByLineId.TryGetValue(line.Id, out var qty) || qty <= 0)
                continue;

            if (unitCostByLineId is not null && unitCostByLineId.TryGetValue(line.Id, out var unitCost))
                line.UpdateUnitCost(unitCost);
            line.Receive(qty);
            anyReceived = true;
        }

        if (!anyReceived)
            throw new DomainException("Debes indicar al menos una línea con cantidad recibida.");

        RecalculateTotals();
        ReceivedAt = receivedAt;
        Status = _lines.All(x => x.PendingQuantity <= 0)
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;
    }

    private void RecalculateTotals()
    {
        SubTotal = _lines.Sum(x => x.SubTotal);
        Tax = _lines.Sum(x => x.TaxAmount);
        Total = SubTotal + Tax;
    }
}
