using System.Security.Cryptography;
using System.Text;

namespace FoodParkDigital.Infrastructure.BackgroundJobs.Inbox;

/// <summary>
/// An authenticated webhook delivery, stored as received and processed in the background with retries.
/// </summary>
/// <remarks>
/// Infrastructure only: a delivery can carry messages for several tenants, which are resolved while it is
/// processed. Payloads contain phone numbers and message text, so processed rows are deleted after the
/// retention period.
/// </remarks>
public sealed class InboxMessage : QueueItem
{
    private InboxMessage()
    {
    }

    /// <param name="deliveryKey">What identifies the delivery when not the whole payload (see IWebhookInbox).</param>
    public InboxMessage(string source, string payload, DateTimeOffset receivedAt, string? deliveryKey = null)
    {
        Id = Guid.NewGuid();
        Source = source;
        Payload = payload;
        PayloadHash = HashOf(deliveryKey ?? payload);
        CreatedAt = receivedAt;
        NextAttemptAt = receivedAt;
    }

    public string Source { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;

    /// <summary>SHA-256 of the payload: a provider retry of the same delivery is stored once.</summary>
    public string PayloadHash { get; private set; } = string.Empty;

    public static string HashOf(string payload)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
