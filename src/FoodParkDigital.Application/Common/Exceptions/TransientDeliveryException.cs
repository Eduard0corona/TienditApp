namespace FoodParkDigital.Application.Common.Exceptions;

/// <summary>
/// Thrown by a background handler when a provider may accept the same message later (timeout, throttling,
/// a provider error). The outbox dispatcher retries the handler with backoff.
/// </summary>
public sealed class TransientDeliveryException : Exception
{
    public TransientDeliveryException(string message)
        : base(message)
    {
    }
}
