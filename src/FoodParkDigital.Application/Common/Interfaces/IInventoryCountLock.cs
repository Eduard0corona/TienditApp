namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// Puts in line what changes the physical count of a location (Fase 27A, ADR 0037): inside the transaction of the request,
/// starting a count, recording or adding items, posting and cancelling wait for one another, so a capture never slips into
/// a count that is being posted and a location never gets two open counts.
/// </summary>
public interface IInventoryCountLock
{
    /// <summary>Holds the count lock of the location until the request's transaction ends.</summary>
    Task AcquireAsync(Guid tenantId, Guid inventoryLocationId, CancellationToken cancellationToken);
}
