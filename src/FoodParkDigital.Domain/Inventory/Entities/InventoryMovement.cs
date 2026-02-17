using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Inventory.Enums;

namespace FoodParkDigital.Domain.Inventory.Entities;

public class InventoryMovement : AuditableEntity, ITenantScoped
{
    private readonly List<InventoryMovementLine> _lines = [];

    private InventoryMovement() { }

    public InventoryMovement(
        Guid tenantId,
        string movementNumber,
        InventoryMovementType movementType,
        InventoryMovementReason movementReason,
        DateTimeOffset movementDate,
        string? referenceNumber = null,
        string? externalReference = null,
        string? counterpartyName = null,
        string? documentNumber = null,
        string? notes = null)
    {
        TenantId = tenantId;
        MovementNumber = movementNumber;
        MovementType = movementType;
        MovementReason = movementReason;
        MovementDate = movementDate;
        ReferenceNumber = referenceNumber;
        ExternalReference = externalReference;
        CounterpartyName = counterpartyName;
        DocumentNumber = documentNumber;
        Notes = notes;
    }

    public Guid TenantId { get; private set; }
    public string MovementNumber { get; private set; } = null!;
    public InventoryMovementType MovementType { get; private set; }
    public InventoryMovementReason MovementReason { get; private set; }
    public DateTimeOffset MovementDate { get; private set; }
    public string? ReferenceNumber { get; private set; }
    public string? ExternalReference { get; private set; }
    public string? CounterpartyName { get; private set; }
    public string? DocumentNumber { get; private set; }
    public string? Notes { get; private set; }
    public decimal TotalAmount { get; private set; }

    public IReadOnlyCollection<InventoryMovementLine> Lines => _lines.AsReadOnly();

    public InventoryMovementLine AddLine(
        Guid tenantId,
        Guid supplyItemId,
        Guid inventoryLocationId,
        decimal quantity,
        decimal? unitCost = null,
        string? lotNumber = null,
        DateTimeOffset? expirationDate = null,
        string? notes = null)
    {
        var line = new InventoryMovementLine(
            tenantId: tenantId,
            movementId: Id,
            supplyItemId: supplyItemId,
            inventoryLocationId: inventoryLocationId,
            quantity: quantity,
            unitCost: unitCost,
            lotNumber: lotNumber,
            expirationDate: expirationDate,
            notes: notes);

        _lines.Add(line);
        RecalculateTotalAmount();
        return line;
    }

    public void RecalculateTotalAmount()
    {
        TotalAmount = _lines.Sum(x => x.TotalCost ?? 0);
    }
}
