namespace FoodParkDigital.Application.Common.Models;

/// <summary>Outcome of handing a message to a provider (WhatsApp, SMS).</summary>
public sealed class MessageSendResult : Result
{
    private MessageSendResult(bool isSuccess, string? error, bool isTransient, string? providerMessageId, bool notUploaded = false)
        : base(isSuccess, error)
    {
        IsTransient = isTransient;
        ProviderMessageId = providerMessageId;
        NotUploaded = notUploaded;
    }

    /// <summary>
    /// A document the provider did not take (Fase 31M): no message was sent, so a sender may still send it another way
    /// (the quote's text without its PDF) without the customer getting it twice.
    /// </summary>
    public bool NotUploaded { get; }

    /// <summary>
    /// The same message may be accepted later (timeout, no connection, 429 or 5xx): background senders retry it.
    /// A rejected number, an unapproved template or a disabled integration is not transient.
    /// </summary>
    public bool IsTransient { get; }

    /// <summary>Id the provider assigned (WhatsApp <c>wamid</c>), used to match its delivery statuses.</summary>
    public string? ProviderMessageId { get; }

    public static MessageSendResult Sent(string? providerMessageId = null) => new(true, null, false, providerMessageId);

    public static MessageSendResult Failed(string error, bool isTransient) => new(false, error, isTransient, null);

    /// <summary>The document of a message could not be uploaded: nothing reached the customer.</summary>
    public static MessageSendResult UploadFailed(string error, bool isTransient) => new(false, error, isTransient, null, notUploaded: true);
}
