using System.Globalization;
using System.Text;

namespace FoodParkDigital.Application.Tenants;

/// <summary>
/// The giros that change what a business starts with. A giro is the text the sign-up picks from its list (the client's
/// INDUSTRY_OPTIONS), compared without accents or capitals.
/// </summary>
public static class BusinessIndustries
{
    /// <summary>A kitchen that only delivers, without premises open to the public (a dark kitchen, often a home).</summary>
    public const string HomeDelivery = "Comida a domicilio";

    /// <summary>A business of this giro starts with a main branch that only delivers, so its address is never published.</summary>
    public static bool OnlyDelivers(string? industry) => Comparable(industry) == Comparable(HomeDelivery);

    private static string Comparable(string? value)
    {
        var decomposed = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormD);
        var letters = decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);
        return new string(letters.ToArray()).ToLowerInvariant();
    }
}
