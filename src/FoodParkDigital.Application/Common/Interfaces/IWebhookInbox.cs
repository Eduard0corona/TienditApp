namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// Stores authenticated webhook deliveries so they are acknowledged at once and processed in the
/// background, with retries, instead of inside the provider's request.
/// </summary>
public interface IWebhookInbox
{
    /// <returns>False when the exact same delivery was already stored (a provider retry).</returns>
    Task<bool> EnqueueAsync(string source, string payload, CancellationToken cancellationToken = default)
        => EnqueueAsync(source, payload, deliveryKey: null, cancellationToken);

    /// <param name="deliveryKey">
    /// What identifies a delivery when the payload also carries what the provider does not sign: a delivery with the same
    /// key is the same one, whatever else its payload says. Null: the payload itself.
    /// </param>
    /// <returns>False when the same delivery was already stored (a provider retry, or a replay).</returns>
    Task<bool> EnqueueAsync(string source, string payload, string? deliveryKey, CancellationToken cancellationToken);
}

public static class WebhookSources
{
    public const string WhatsApp = "whatsapp";

    /// <summary>Mercado Pago's notifications of payments (Fase 24I).</summary>
    public const string MercadoPago = "mercadopago";
}
