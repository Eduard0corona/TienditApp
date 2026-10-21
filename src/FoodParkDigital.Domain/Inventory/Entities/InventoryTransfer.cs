using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Enums;

namespace FoodParkDigital.Domain.Inventory.Entities;

/// <summary>
/// A transfer between two locations of the business (Fase 27B, ADR 0038): what left one and entered the other at once,
/// as a <see cref="InventoryMovementType.TransferOut"/> and a <see cref="InventoryMovementType.TransferIn"/> of reason
/// <see cref="InventoryMovementReason.Transfer"/> posted in the same transaction. Made, it does not change: undoing it
/// is another transfer the other way.
/// </summary>
public class InventoryTransfer : AuditableEntity, ITenantScoped
{
    public const int MaxLines = 200;
    public const int MaxNotesLength = 500;
    public const int MaxUserNameLength = 256;

    public const string SameLocationMessage = "Elige una ubicación de destino distinta a la de origen.";
    public const string TooManyLinesMessage = "Una transferencia lleva como máximo 200 artículos.";

    private InventoryTransfer() { }

    /// <param name="id">Known before the movements are posted: their external references carry it.</param>
    public InventoryTransfer(
        Guid id,
        Guid tenantId,
        string transferNumber,
        Guid idempotencyKey,
        Guid fromLocationId,
        Guid toLocationId,
        DateTimeOffset transferredAt,
        string transferredBy,
        Guid outMovementId,
        Guid inMovementId,
        int lineCount,
        decimal totalCost,
        string? notes = null)
    {
        if (id == Guid.Empty)
            throw new DomainException("La transferencia necesita su identificador.");
        if (string.IsNullOrWhiteSpace(transferNumber))
            throw new DomainException("La transferencia necesita su folio.");
        if (idempotencyKey == Guid.Empty)
            throw new DomainException("La transferencia necesita su llave.");
        if (fromLocationId == Guid.Empty || toLocationId == Guid.Empty)
            throw new DomainException("Elige la ubicación de origen y la de destino.");
        if (fromLocationId == toLocationId)
            throw new DomainException(SameLocationMessage);
        if (outMovementId == Guid.Empty || inMovementId == Guid.Empty || outMovementId == inMovementId)
            throw new DomainException("La transferencia necesita su salida y su entrada.");
        if (lineCount is < 1 or > MaxLines)
            throw new DomainException(lineCount < 1 ? "La transferencia necesita al menos un artículo." : TooManyLinesMessage);
        if (totalCost < 0)
            throw new DomainException("El valor de la transferencia no puede ser negativo.");
        if (string.IsNullOrWhiteSpace(transferredBy))
            throw new DomainException("Falta quién hizo la transferencia.");

        Id = id;
        TenantId = tenantId;
        TransferNumber = transferNumber;
        IdempotencyKey = idempotencyKey;
        FromLocationId = fromLocationId;
        ToLocationId = toLocationId;
        TransferredAt = transferredAt;
        var user = transferredBy.Trim();
        TransferredBy = user.Length > MaxUserNameLength ? user[..MaxUserNameLength] : user;
        OutMovementId = outMovementId;
        InMovementId = inMovementId;
        LineCount = lineCount;
        TotalCost = totalCost;
        var trimmed = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Notes = trimmed is { Length: > MaxNotesLength } ? trimmed[..MaxNotesLength] : trimmed;
    }

    public Guid TenantId { get; private set; }
    /// <summary>TRF-20261021-0001.</summary>
    public string TransferNumber { get; private set; } = null!;
    /// <summary>Sent by the app for each transfer: the same one again answers this transfer without moving anything.</summary>
    public Guid IdempotencyKey { get; private set; }
    public Guid FromLocationId { get; private set; }
    public Guid ToLocationId { get; private set; }
    public DateTimeOffset TransferredAt { get; private set; }
    public string TransferredBy { get; private set; } = null!;
    public string? Notes { get; private set; }
    /// <summary>The TransferOut that took the items from the origin.</summary>
    public Guid OutMovementId { get; private set; }
    /// <summary>The TransferIn that put them in the destination.</summary>
    public Guid InMovementId { get; private set; }
    public int LineCount { get; private set; }
    /// <summary>What the items were worth: the same in both movements.</summary>
    public decimal TotalCost { get; private set; }
}
