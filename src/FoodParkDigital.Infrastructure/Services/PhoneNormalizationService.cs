using System.Text.RegularExpressions;
using FoodParkDigital.Application.Common.Interfaces;

namespace FoodParkDigital.Infrastructure.Services;

public class PhoneNormalizationService : IPhoneNormalizationService
{
    public string NormalizeToE164(string rawPhone, string defaultCountry = "MX")
    {
        if (string.IsNullOrWhiteSpace(rawPhone))
            throw new ArgumentException("Phone is required.", nameof(rawPhone));

        var compact = rawPhone.Trim();
        compact = compact.StartsWith("00", StringComparison.Ordinal)
            ? $"+{compact[2..]}"
            : compact;

        if (compact.StartsWith('+'))
        {
            var digits = Regex.Replace(compact[1..], @"\D", "");
            return ValidateE164(digits);
        }

        var normalizedDigits = Regex.Replace(compact, @"\D", "");
        if (string.IsNullOrEmpty(normalizedDigits))
            throw new ArgumentException("Phone contains no digits.", nameof(rawPhone));

        var country = defaultCountry.ToUpperInvariant();
        var e164Digits = country switch
        {
            "US" when normalizedDigits.Length == 10 => $"1{normalizedDigits}",
            "US" when normalizedDigits.Length == 11 && normalizedDigits.StartsWith("1", StringComparison.Ordinal) => normalizedDigits,
            "MX" when normalizedDigits.Length == 10 => $"52{normalizedDigits}",
            "MX" when normalizedDigits.Length == 12 && normalizedDigits.StartsWith("52", StringComparison.Ordinal) => normalizedDigits,
            _ => normalizedDigits
        };

        return ValidateE164(e164Digits);
    }

    private static string ValidateE164(string digits)
    {
        if (digits.Length is < 8 or > 15)
            throw new ArgumentException("Phone must contain between 8 and 15 digits in E.164 format.");

        if (digits[0] == '0')
            throw new ArgumentException("E.164 phone cannot start with zero after country code.");

        return $"+{digits}";
    }
}
