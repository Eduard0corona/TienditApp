namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// Puts in line the orders AI assistants prepare for a business (Fase 32B, ADR 0096): inside the transaction of the request,
/// two at once wait for each other, so both do not pass the last place of the ones it may have waiting.
/// </summary>
public interface IAssistantOrdersLock
{
    /// <summary>Holds the business's lock of prepared orders until the request's transaction ends.</summary>
    Task AcquireAsync(Guid tenantId, CancellationToken cancellationToken);
}
