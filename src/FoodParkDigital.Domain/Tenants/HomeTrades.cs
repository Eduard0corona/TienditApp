using System.Globalization;
using System.Text;

namespace FoodParkDigital.Domain.Tenants;

/// <summary>
/// The giros of trades that go to the customer's home to check, quote and do the work (ADR 0053): plumbers, electricians
/// and the like. Their category in Explorar is «Hogar y reparaciones», they start receiving visits to quote and, except
/// the ones that usually have a workshop, without a published address. A giro is the text the sign-up picks from the
/// client's INDUSTRY_OPTIONS, compared without accents or capitals.
/// </summary>
public static class HomeTrades
{
    public const string Carpentry = "Carpintería";
    public const string Welding = "Herrería y soldadura";
    public const string Electrician = "Electricista";
    public const string Plumbing = "Plomería";
    public const string AirConditioning = "Aire acondicionado";
    public const string PestControl = "Fumigación";
    public const string Refrigeration = "Refrigeración y línea blanca";
    public const string Locksmith = "Cerrajería";
    public const string Gardening = "Jardinería";
    public const string HomeCleaning = "Limpieza del hogar";
    public const string Painting = "Pintura e impermeabilización";

    /// <summary>Every trade giro, in the order of the sign-up list.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Carpentry, Welding, Electrician, Plumbing, AirConditioning,
        PestControl, Refrigeration, Locksmith, Gardening, HomeCleaning, Painting
    ];

    /// <summary>The trades that usually have a workshop: they keep the premises of their main branch when they sign up.</summary>
    public static readonly IReadOnlyList<string> WithWorkshop = [Carpentry, Welding];

    public static bool IsHomeTrade(string? industry) => All.Any(giro => Comparable(giro) == Comparable(industry));

    /// <summary>A trade without a workshop: it starts with a main branch that only goes to its customers.</summary>
    public static bool StartsWithoutPremises(string? industry)
        => IsHomeTrade(industry) && !WithWorkshop.Any(giro => Comparable(giro) == Comparable(industry));

    private static string Comparable(string? value)
    {
        var decomposed = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormD);
        var letters = decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);
        return new string(letters.ToArray()).ToLowerInvariant();
    }
}
