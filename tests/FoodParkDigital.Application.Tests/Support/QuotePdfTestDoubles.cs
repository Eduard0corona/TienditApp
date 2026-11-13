using System.Text;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Quotes;

namespace FoodParkDigital.Application.Tests.Support;

/// <summary>Renders a stand-in PDF of each quote it is given and keeps what it was given (Fase 31M); or fails, when told.</summary>
internal sealed class RecordingQuotePdfRenderer : IQuotePdfRenderer
{
    public List<QuotePdfModel> Rendered { get; } = [];

    /// <summary>Thrown by every render from now on.</summary>
    public Exception? Failure { get; set; }

    /// <summary>The stand-in PDF of <paramref name="quote"/>: its folio and version, to tell one from another.</summary>
    public static byte[] Pdf(QuotePdfModel quote) => Encoding.ASCII.GetBytes($"%PDF-1.4 {quote.Number} v{quote.Revision}");

    public Task<byte[]> RenderAsync(QuotePdfModel quote, CancellationToken cancellationToken)
    {
        if (Failure is not null)
            throw Failure;
        Rendered.Add(quote);
        return Task.FromResult(Pdf(quote));
    }
}

/// <summary>Keeps the WhatsApp documents it is asked to send and says they went.</summary>
internal sealed class RecordingWhatsAppDocuments : IWhatsAppDocumentSender
{
    public List<(string Phone, WhatsAppDocument Document, string Caption, string? TemplateName)> Sent { get; } = [];

    public Task<MessageSendResult> SendDocumentAsync(
        Guid tenantId, string phoneNumber, WhatsAppDocument document, string caption, string? templateName, string languageCode,
        string[] parameters, CancellationToken cancellationToken = default)
    {
        Sent.Add((phoneNumber, document, caption, templateName));
        return Task.FromResult(MessageSendResult.Sent($"wamid.doc.{Sent.Count}"));
    }
}
