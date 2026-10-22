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

    private static readonly string[] CampaignPlaceholders = [Name, Business];
    private static readonly string[] ReminderPlaceholders = [Name, Business, Service, Date, Time];
    // {fecha} is the oldest last day to pay that passed.
    private static readonly string[] CreditReminderPlaceholders = [Name, Business, Amount, Overdue, Date];
    // {fecha} is the coming last day to pay.
    private static readonly string[] CreditDueSoonPlaceholders = [Name, Business, Amount, DueSoon, Date];

    [GeneratedRegex(@"\{([a-zA-Z_]+)\}")]
    private static partial Regex PlaceholderPattern();

    public static IReadOnlyList<string> AllowedPlaceholders(MessagePurpose purpose) => purpose switch
    {
        MessagePurpose.AppointmentReminder => ReminderPlaceholders,
        MessagePurpose.CreditReminder => CreditReminderPlaceholders,
        MessagePurpose.CreditDueSoon => CreditDueSoonPlaceholders,
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
    }

    public static string Render(string body, IReadOnlyDictionary<string, string> values)
        => PlaceholderPattern().Replace(body, match =>
            values.TryGetValue(match.Groups[1].Value.ToLowerInvariant(), out var value) ? value : match.Value);

    /// <summary>The values for a template approved in Meta, in the order of <see cref="GetPlaceholders"/>.</summary>
    public static string[] Parameters(string body, IReadOnlyDictionary<string, string> values)
        => GetPlaceholders(body)
            .Select(placeholder => values.TryGetValue(placeholder, out var value) ? value : string.Empty)
            .ToArray();
}
