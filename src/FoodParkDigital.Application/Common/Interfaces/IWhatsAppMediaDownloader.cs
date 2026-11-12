namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>What downloading a file a customer sent by WhatsApp gave (Fase 31L, ADR 0092).</summary>
public enum WhatsAppMediaDownloadOutcome
{
    /// <summary>Here is its content, no bigger than asked.</summary>
    Downloaded,
    /// <summary>It will not come: WhatsApp no longer has it, refused it, it is bigger than asked or WhatsApp is not connected.</summary>
    Unavailable,
    /// <summary>WhatsApp did not answer now (a timeout, too many requests, a failure of theirs): a later try may get it.</summary>
    Transient
}

public sealed record WhatsAppMediaDownload(WhatsAppMediaDownloadOutcome Outcome, byte[]? Content = null)
{
    public static WhatsAppMediaDownload Unavailable { get; } = new(WhatsAppMediaDownloadOutcome.Unavailable);
    public static WhatsAppMediaDownload Transient { get; } = new(WhatsAppMediaDownloadOutcome.Transient);
}

/// <summary>Downloads, with the WhatsApp of the business in scope, a file its customers sent (Fase 31L, ADR 0092).</summary>
public interface IWhatsAppMediaDownloader
{
    Task<WhatsAppMediaDownload> DownloadAsync(string mediaId, long maxBytes, CancellationToken cancellationToken);
}
