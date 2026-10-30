using FoodParkDigital.Application.Common.Models;

namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// Sends a document with the WhatsApp of the business in scope (Fase 28M, ADR 0058): the PDF of a CFDI. Meta takes it uploaded
/// first and then in a message: in the document header of the approved template <paramref name="templateName"/>, with its
/// body parameters, or without a template as a document with its caption, which WhatsApp only delivers within 24 hours of the
/// customer's last message.
/// </summary>
public interface IWhatsAppDocumentSender
{
    Task<MessageSendResult> SendDocumentAsync(
        Guid tenantId,
        string phoneNumber,
        WhatsAppDocument document,
        string caption,
        string? templateName,
        string languageCode,
        string[] parameters,
        CancellationToken cancellationToken = default);
}
