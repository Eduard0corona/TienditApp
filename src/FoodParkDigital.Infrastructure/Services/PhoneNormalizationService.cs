using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using FoodParkDigital.Application.Common.Interfaces;

namespace FoodParkDigital.Infrastructure.Services;

public class PhoneNormalizationService : IPhoneNormalizationService
{
    public string NormalizeToE164(string rawPhone, string defaultCountry = "MX")
        => TryNormalize(rawPhone, defaultCountry, out var phoneNormalized, out var error)
            ? phoneNormalized
            : throw new ArgumentException(error, nameof(rawPhone));

    public bool TryNormalizeToE164(string? rawPhone, string defaultCountry, [NotNullWhen(true)] out string? phoneNormalized)
    {
        if (TryNormalize(rawPhone, defaultCountry, out var normalized, out _))
        {
            phoneNormalized = normalized;
            return true;
        }

        phoneNormalized = null;
        return false;
    }

    private static bool TryNormalize(string? rawPhone, string defaultCountry, out string phoneNormalized, out string error)
    {
        phoneNormalized = string.Empty;
        if (string.IsNullOrWhiteSpace(rawPhone))
        {
            error = "Phone is required.";
            return false;
        }

        var compact = rawPhone.Trim();
        compact = compact.StartsWith("00", StringComparison.Ordinal)
            ? $"+{compact[2..]}"
            : compact;

        if (compact.StartsWith('+'))
            return TryValidateE164(Regex.Replace(compact[1..], @"\D", ""), out phoneNormalized, out error);

        var normalizedDigits = Regex.Replace(compact, @"\D", "");
        if (string.IsNullOrEmpty(normalizedDigits))
        {
            error = "Phone contains no digits.";
            return false;
        }

        var country = defaultCountry.ToUpperInvariant();
        var e164Digits = country switch
        {
            "US" when normalizedDigits.Length == 10 => $"1{normalizedDigits}",
            "US" when normalizedDigits.Length == 11 && normalizedDigits.StartsWith("1", StringComparison.Ordinal) => normalizedDigits,
            "MX" when normalizedDigits.Length == 10 => $"52{normalizedDigits}",
            "MX" when normalizedDigits.Length == 12 && normalizedDigits.StartsWith("52", StringComparison.Ordinal) => normalizedDigits,
            _ => normalizedDigits
        };

        return TryValidateE164(e164Digits, out phoneNormalized, out error);
    }

    private static bool TryValidateE164(string digits, out string phoneNormalized, out string error)
    {
        phoneNormalized = string.Empty;
        if (digits.Length is < 8 or > 15)
        {
            error = "Phone must contain between 8 and 15 digits in E.164 format.";
            return false;
        }

        if (digits[0] == '0')
        {
            error = "E.164 phone cannot start with zero after country code.";
            return false;
        }

        error = string.Empty;
        phoneNormalized = $"+{digits}";
        return true;
    }
}
