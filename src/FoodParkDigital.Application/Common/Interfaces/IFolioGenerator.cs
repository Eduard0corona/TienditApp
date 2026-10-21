namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// The next folio of a business. Two documents never get the same one, even from two cash registers at the same
/// time; a request that fails after taking its folio may leave a gap.
/// </summary>
public interface IFolioGenerator
{
    /// <summary>ORD-000123.</summary>
    Task<string> NextOrderNumberAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>INV-20261005-0007, numbered by the UTC day of the movement.</summary>
    Task<string> NextInventoryMovementNumberAsync(Guid tenantId, DateTimeOffset movementDate, CancellationToken cancellationToken);

    /// <summary>PO-20261005-0002, numbered by the UTC day of the order.</summary>
    Task<string> NextPurchaseOrderNumberAsync(Guid tenantId, DateTimeOffset orderedAt, CancellationToken cancellationToken);

    /// <summary>CNT-20261021-0001, numbered by the UTC day the physical count started.</summary>
    Task<string> NextInventoryCountNumberAsync(Guid tenantId, DateTimeOffset startedAt, CancellationToken cancellationToken);
}
