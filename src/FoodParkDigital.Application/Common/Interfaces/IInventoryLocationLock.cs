namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// Puts in line what changes a location as a whole (Fase 27A, ADR 0037; Fase 27B, ADR 0038): inside the transaction of the
/// request, starting a count, recording or adding items, posting and cancelling it, and a transfer from or to the location
/// wait for one another. A capture never slips into a count that is being posted, a location never gets two open counts,
/// and two transfers of the same location take turns. Sales do not take it.
/// </summary>
public interface IInventoryLocationLock
{
    /// <summary>
    /// Holds the lock of the location until the request's transaction ends. A request that needs two locations takes
    /// them in order of their id, so two requests that need the same two take turns and never deadlock.
    /// </summary>
    Task AcquireAsync(Guid tenantId, Guid inventoryLocationId, CancellationToken cancellationToken);
}
