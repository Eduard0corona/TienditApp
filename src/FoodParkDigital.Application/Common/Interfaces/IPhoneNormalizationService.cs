using System.Diagnostics.CodeAnalysis;

namespace FoodParkDigital.Application.Common.Interfaces;

public interface IPhoneNormalizationService
{
    /// <summary>Normalizes to E.164; throws <see cref="ArgumentException"/> when the input is not a phone.</summary>
    string NormalizeToE164(string rawPhone, string defaultCountry = "MX");

    /// <summary>Normalizes typed input without throwing: false when it is not a valid phone number.</summary>
    bool TryNormalizeToE164(string? rawPhone, string defaultCountry, [NotNullWhen(true)] out string? phoneNormalized);
}
