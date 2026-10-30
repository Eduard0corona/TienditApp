using System.Text.RegularExpressions;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Messaging.Enums;

namespace FoodParkDigital.Domain.Messaging;

/// <summary>
/// Placeholders of message templates, written as <c>{nombre}</c>. A template approved in Meta receives them as
/// its parameters in the order they first appear in the text ({{1}}, {{2}}…).
/// </summary>
public static partial class MessageTemplateText
{
    public const string Name = "nombre";
    public const string Business = "negocio";
    public const string Service = "servicio";
    public const string Date = "fecha";
    public const string Time = "hora";
    /// <summary>What the customer owes in all (Fase 26C).</summary>
    public const string Amount = "monto";
    /// <summary>What of it is past the term (Fase 26C).</summary>
    public const string Overdue = "vencido";
    /// <summary>What of it comes due on the coming last day to pay (Fase 26E).</summary>
    public const string DueSoon = "porvencer";
    /// <summary>Which CFDI and its number, «la factura F-123» (Fase 28M).</summary>
    public const string Document = "comprobante";
    /// <summary>The link to download the XML and the PDF of a CFDI (Fase 28M).</summary>
    public const string Link = "enlace";

    private static readonly string[] CampaignPlaceholders = [Name, Business];
    private static readonly string[] ReminderPlaceholders = [Name, Business, Service, Date, Time];
    // {fecha} is the oldest last day to pay that passed.
    private static readonly string[] CreditReminderPlaceholders = [Name, Business, Amount, Overdue, Date];
    // {fecha} is the coming last day to pay.
    private static readonly string[] CreditDueSoonPlaceholders = [Name, Business, Amount, DueSoon, Date];
    // {nombre} is the receiver of the CFDI, {monto} its total (the payment of a complement) and {fecha} the day it was issued.
    private static readonly string[] CfdiDocumentPlaceholders = [Name, Business, Document, Amount, Date, Link];

    [GeneratedRegex(@"\{([a-zA-Z_]+)\}")]
    private static partial Regex PlaceholderPattern();

    public static IReadOnlyList<string> AllowedPlaceholders(MessagePurpose purpose) => purpose switch
    {
        MessagePurpose.AppointmentReminder => ReminderPlaceholders,
        MessagePurpose.CreditReminder => CreditReminderPlaceholders,
        MessagePurpose.CreditDueSoon => CreditDueSoonPlaceholders,
        MessagePurpose.CfdiDocument => CfdiDocumentPlaceholders,
        _ => CampaignPlaceholders
    };

    /// <summary>The placeholders of the text, once each, in the order they first appear.</summary>
    public static IReadOnlyList<string> GetPlaceholders(string body)
        => PlaceholderPattern().Matches(body)
            .Select(match => match.Groups[1].Value.ToLowerInvariant())
            .Distinct()
            .ToList();

    public static void Validate(string body, MessagePurpose purpose)
    {
        var allowed = AllowedPlaceholders(purpose);
        var unknown = GetPlaceholders(body).Where(placeholder => !allowed.Contains(placeholder)).ToList();
        if (unknown.Count > 0)
            throw new DomainException(
                $"Variables no disponibles: {string.Join(", ", unknown.Select(p => $"{{{p}}}"))}. "
                + $"Usa {string.Join(", ", allowed.Select(p => $"{{{p}}}"))}.");
        // The XML of a CFDI is downloaded only with its link (Fase 28M): a template of invoices that leaves it out would
        // deliver the PDF alone.
        if (purpose == MessagePurpose.CfdiDocument && !GetPlaceholders(body).Contains(Link))
            throw new DomainException(MissingLinkMessage);
    }

    public const string MissingLinkMessage =
        "El mensaje de una factura lleva {enlace}: con él tu cliente descarga el XML, que WhatsApp no manda como documento.";

    public static string Render(string body, IReadOnlyDictionary<string, string> values)
        => PlaceholderPattern().Replace(body, match =>
            values.TryGetValue(match.Groups[1].Value.ToLowerInvariant(), out var value) ? value : match.Value);

    /// <summary>The values for a template approved in Meta, in the order of <see cref="GetPlaceholders"/>.</summary>
    public static string[] Parameters(string body, IReadOnlyDictionary<string, string> values)
        => GetPlaceholders(body)
            .Select(placeholder => values.TryGetValue(placeholder, out var value) ? value : string.Empty)
            .ToArray();
}
