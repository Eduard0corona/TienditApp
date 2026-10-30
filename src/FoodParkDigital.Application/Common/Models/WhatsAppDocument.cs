namespace FoodParkDigital.Application.Common.Models;

/// <summary>A file a WhatsApp message carries as a document (Fase 28M): the PDF of a CFDI, with the name the customer sees.</summary>
public sealed record WhatsAppDocument(byte[] Content, string ContentType, string FileName);
