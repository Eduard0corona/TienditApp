namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// Stores authenticated webhook deliveries so they are acknowledged at once and processed in the
/// background, with retries, instead of inside the provider's request.
/// </summary>
public interface IWebhookInbox
{
    /// <returns>False when the exact same delivery was already stored (a provider retry).</returns>
    Task<bool> EnqueueAsync(string source, string payload, CancellationToken cancellationToken = default);
}

public static class WebhookSources
{
    public const string WhatsApp = "whatsapp";
}
