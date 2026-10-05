using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Messaging;

namespace FoodParkDigital.Application.Customers;

/// <summary>
/// Customer phones are kept in one form (E.164, Mexican mobiles without the 1 WhatsApp adds) so the chat,
/// the orders and the customers list find the same person.
/// </summary>
public static class CustomerPhones
{
    public const string InvalidMessage = "Escribe el teléfono a 10 dígitos o con lada internacional (+52…).";

    public static bool TryNormalize(IPhoneNormalizationService phones, string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (!phones.TryNormalizeToE164(raw, "MX", out var e164))
            return false;
        normalized = MessagingPhone.ToKey(e164);
        return true;
    }

    /// <summary>
    /// The forms a customer record may hold this number in: the normalized ones and, for records saved before
    /// phones were normalized, the ten national digits.
    /// </summary>
    public static IReadOnlyList<string> StoredForms(string normalized)
    {
        var forms = MessagingPhone.Variants(normalized).ToList();
        if (normalized.StartsWith("+52", StringComparison.Ordinal) && normalized.Length == 13)
            forms.Add(normalized[3..]);
        return forms;
    }
}
