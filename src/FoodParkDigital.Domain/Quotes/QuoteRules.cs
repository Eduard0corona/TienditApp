namespace FoodParkDigital.Domain.Quotes;

/// <summary>The limits and the messages of the quotes (Fase 29C, ADR 0053 points 14 to 17).</summary>
public static class QuoteRules
{
    public const int DefaultValidityDays = 15;
    public const int MinValidityDays = 1;
    public const int MaxValidityDays = 90;
    /// <summary>How long after its validity the link of a quote still opens (as the link of an order, ADR 0023).</summary>
    public const int LinkDaysAfterValidity = 30;

    public const int MaxLines = 50;
    public const int MaxDescriptionLength = 200;
    public const int MaxNoteLength = 1000;
    public const int MaxResponseLength = 500;
    public const int MaxCustomerNameLength = 200;
    public const int MaxCustomerPhoneLength = 50;
    public const decimal MaxQuantity = 10_000m;
    public const decimal MaxUnitPrice = 1_000_000m;

    public const string ValidityDaysMessage = "La vigencia de una cotización va de 1 a 90 días.";
    public const string CustomerNameMessage = "Escribe el nombre del cliente.";
    public const string CustomerNameTooLongMessage = "El nombre del cliente admite hasta 200 caracteres.";
    public const string CustomerPhoneMessage = "Escribe el teléfono del cliente: con él le llega la cotización.";
    public const string NoteTooLongMessage = "La nota admite hasta 1,000 caracteres.";
    public const string TooManyLinesMessage = "Una cotización admite hasta 50 renglones.";
    public const string DescriptionMessage = "Cada renglón necesita qué incluye.";
    public const string DescriptionTooLongMessage = "Lo que incluye un renglón admite hasta 200 caracteres.";
    public const string QuantityMessage = "La cantidad de cada renglón va de 0.001 a 10,000, con hasta tres decimales.";
    public const string UnitPriceMessage = "El precio de cada renglón va de $0 a $1,000,000, con hasta dos decimales.";
    public const string ResponseTooLongMessage = "Lo que respondió el cliente admite hasta 500 caracteres.";
    public const string ReplyChannelMessage = "Elige por dónde respondió el cliente.";

    public const string NotDraftMessage = "Esta versión ya se envió y no se edita: corrígela para hacer una versión nueva.";
    public const string NothingToSendMessage = "Agrega al menos un renglón con precio antes de enviarla.";
    public const string NotSentMessage = "Solo se registra la respuesta de una cotización enviada.";
    public const string ExpiredMessage = "La cotización ya venció: corrígela para mandarle una versión nueva con otra vigencia.";
    public const string CannotReviseMessage = "Solo se corrige la versión vigente de una cotización enviada o rechazada.";
    public const string NotReplaceableMessage = "La versión anterior ya cambió: el cliente la aceptó o ya hay otra versión enviada.";
    public const string DraftOnlyDiscardMessage = "Solo se descarta un borrador.";
    public const string VisitCreditMessage = "El descuento de la visita sale de la venta de su cita.";
}
