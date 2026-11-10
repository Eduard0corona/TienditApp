namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// Fase 31A (ADR 0083): tells a signed-in customer that one of their orders changed, so «Mis pedidos» reads it again
/// through <c>/api/v1/me</c>, which checks the order is theirs. Only the id of the order goes out: nothing of the
/// business, of the order or of anyone else.
/// </summary>
public interface ICustomerOrderNotificationService
{
    Task NotifyOrderChanged(Guid customerUserId, Guid orderId);
}
