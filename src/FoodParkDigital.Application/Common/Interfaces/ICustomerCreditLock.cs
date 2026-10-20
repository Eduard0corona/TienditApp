namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// Puts in line what changes what a customer owes on credit (Fase 26A, ADR 0033): inside the transaction of the request, a
/// second sale on credit or account payment for the same customer waits until the first commits, so it counts what the first
/// one sold or paid before checking the limit or splitting the payment.
/// </summary>
public interface ICustomerCreditLock
{
    /// <summary>Holds the lock of the customer with this phone in the business until the request's transaction ends.</summary>
    Task AcquireAsync(Guid tenantId, string phoneKey, CancellationToken cancellationToken);
}
