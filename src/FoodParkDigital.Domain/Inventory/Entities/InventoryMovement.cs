using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
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
        if (movementReason == InventoryMovementReason.LedgerReconciliation
            && movementType is not (InventoryMovementType.AdjustmentIncrease or InventoryMovementType.AdjustmentDecrease))
            throw new DomainException("Un cuadre del historial es un ajuste: suma o resta al historial.");

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

    /// <summary>
    /// A line of a reconciliation of the history (Fase 27C, ADR 0039): it records what the history of a stock was missing
    /// to add up to it and does not move the stock, so it keeps the stock before and after and carries no cost.
    /// </summary>
    public InventoryMovementLine AddReconciliationLine(
        Guid tenantId, Guid supplyItemId, Guid inventoryLocationId, decimal quantity, decimal onHand, string? notes = null)
    {
        if (MovementReason != InventoryMovementReason.LedgerReconciliation)
            throw new DomainException("Solo un cuadre del historial anota sin mover la existencia.");

        var line = AddLine(tenantId, supplyItemId, inventoryLocationId, quantity, notes: notes);
        line.SetBalances(onHand, onHand);
        return line;
    }

    public void RecalculateTotalAmount()
    {
        TotalAmount = _lines.Sum(x => x.TotalCost ?? 0);
    }
}
