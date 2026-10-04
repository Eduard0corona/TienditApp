namespace FoodParkDigital.Application.Common.Models;

/// <summary>Outcome of handing a message to a provider (WhatsApp, SMS).</summary>
public sealed class MessageSendResult : Result
{
    private MessageSendResult(bool isSuccess, string? error, bool isTransient, string? providerMessageId)
        : base(isSuccess, error)
    {
        IsTransient = isTransient;
        ProviderMessageId = providerMessageId;
    }

    /// <summary>
    /// The same message may be accepted later (timeout, no connection, 429 or 5xx): background senders retry it.
    /// A rejected number, an unapproved template or a disabled integration is not transient.
    /// </summary>
    public bool IsTransient { get; }

    /// <summary>Id the provider assigned (WhatsApp <c>wamid</c>), used to match its delivery statuses.</summary>
    public string? ProviderMessageId { get; }

    public static MessageSendResult Sent(string? providerMessageId = null) => new(true, null, false, providerMessageId);

    public static MessageSendResult Failed(string error, bool isTransient) => new(false, error, isTransient, null);
}
