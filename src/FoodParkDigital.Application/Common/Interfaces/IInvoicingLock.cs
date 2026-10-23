namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// Puts in line what decides which invoice covers a sale (Fase 28B, ADR 0046): inside the transaction of the request, the
/// invoice of an order and the global invoice of a period of the same business wait for each other, so a sale never ends
/// up in both. Each checks, once it holds the lock, that the other did not take the sale first.
/// </summary>
public interface IInvoicingLock
{
    /// <summary>Holds the invoicing lock of the business until the request's transaction ends.</summary>
    Task AcquireAsync(Guid tenantId, CancellationToken cancellationToken);
}
