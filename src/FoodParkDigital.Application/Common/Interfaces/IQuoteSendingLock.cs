namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// Puts in line the sends of a business's quotes (Fase 29D, ADR 0053 point 16): inside the transaction of the request, two
/// «Mandar por correo» or «Mandar por WhatsApp» at once wait for each other, so both do not pass the last one of a limit.
/// </summary>
public interface IQuoteSendingLock
{
    /// <summary>Holds the business's lock of quote sends until the request's transaction ends.</summary>
    Task AcquireAsync(Guid tenantId, CancellationToken cancellationToken);
}
