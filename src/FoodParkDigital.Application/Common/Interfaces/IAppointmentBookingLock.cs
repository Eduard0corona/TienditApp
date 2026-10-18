namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// Puts the online bookings of a business in line (Fase 24D, ADR 0025): inside the transaction of the request, the second
/// one waits until the first commits, so it counts the appointment the first one added before taking a time.
/// </summary>
public interface IAppointmentBookingLock
{
    /// <summary>Holds the business's booking lock until the request's transaction ends.</summary>
    Task AcquireAsync(Guid tenantId, CancellationToken cancellationToken);
}
