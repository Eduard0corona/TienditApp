using System.Diagnostics.CodeAnalysis;
using FoodParkDigital.Application.Common.Interfaces;

namespace FoodParkDigital.Application.Common.Rules;

public static class PhoneNumberRules
{
    /// <summary>Message for a typed phone that does not normalize to E.164 (answered as 400, not 500).</summary>
    public const string InvalidPhoneMessage =
        "El teléfono no es válido. Escribe 10 dígitos o el número con lada internacional (por ejemplo +52 55 1234 5678).";

    /// <summary>
    /// The phone of a new account, in E.164. Without its international prefix (+ or 00) only a Mexican number is taken:
    /// the ten digits, or 52 and the ten. Any other length is a mistyped number, never one of another country that would
    /// get the account's SMS codes (551234567 is not +551234567). Its digits are ASCII: the service keeps any Unicode
    /// digit (٠, U+0660, counts as one for it).
    /// </summary>
    public static bool TryNormalizeSignUpPhone(
        IPhoneNormalizationService phones, string? rawPhone, [NotNullWhen(true)] out string? phoneNormalized)
    {
        phoneNormalized = null;
        var typed = rawPhone?.Trim() ?? string.Empty;
        if (typed.Any(c => char.IsDigit(c) && !char.IsAsciiDigit(c)))
            return false;

        if (!typed.StartsWith('+') && !typed.StartsWith("00", StringComparison.Ordinal))
        {
            var digits = new string(typed.Where(char.IsAsciiDigit).ToArray());
            if (digits.Length != 10 && !(digits.Length == 12 && digits.StartsWith("52", StringComparison.Ordinal)))
                return false;
        }

        return phones.TryNormalizeToE164(typed, "MX", out phoneNormalized);
    }

    /// <summary>
    /// A phone typed by a person of the business for someone else (the WhatsApp of an invoice, Fase 28M), in E.164, with the
    /// rule of an account's phone: a Mexican number without its international prefix, any other with it.
    /// </summary>
    public static bool TryNormalizeTypedPhone(
        IPhoneNormalizationService phones, string? rawPhone, [NotNullWhen(true)] out string? phoneNormalized)
        => TryNormalizeSignUpPhone(phones, rawPhone, out phoneNormalized);
}
