using System.Globalization;
using System.Text;

namespace FoodParkDigital.Domain.Tenants.Enums;

/// <summary>
/// The category a business starts with in Explorar: the one of its giro (the text the sign-up picks from the client's
/// INDUSTRY_OPTIONS, compared without accents or capitals), or else the one of its type. The migration that added the
/// category gave each existing business the same one, with the giros the list had then (AddBusinessDirectory).
/// </summary>
public static class BusinessCategoryDefaults
{
    private static readonly IReadOnlyDictionary<string, BusinessCategory> ByIndustry = new Dictionary<string, BusinessCategory>
    {
        [Comparable("Alimentos y bebidas")] = BusinessCategory.Food,
        [Comparable("Comida a domicilio")] = BusinessCategory.Food,
        [Comparable("Abarrotes")] = BusinessCategory.Groceries,
        [Comparable("Farmacia")] = BusinessCategory.Health,
        [Comparable("Consultorio")] = BusinessCategory.Health,
        [Comparable("Consultorio dental")] = BusinessCategory.Health,
        [Comparable("Ropa y accesorios")] = BusinessCategory.Apparel,
        [Comparable("Electrónica")] = BusinessCategory.Electronics,
        [Comparable("Ferretería")] = BusinessCategory.Hardware,
        [Comparable("Papelería")] = BusinessCategory.Stationery,
        [Comparable("Estética y spa")] = BusinessCategory.Beauty,
        [Comparable("Gimnasio")] = BusinessCategory.Beauty,
        [Comparable("Taller mecánico")] = BusinessCategory.Automotive,
        [Comparable("Carpintería")] = BusinessCategory.Services,
        [Comparable("Herrería y soldadura")] = BusinessCategory.Services,
        [Comparable("Electricista")] = BusinessCategory.Services,
        [Comparable("Plomería")] = BusinessCategory.Services,
        [Comparable("Aire acondicionado")] = BusinessCategory.Services,
        [Comparable("Servicios profesionales")] = BusinessCategory.Services,
        [Comparable("Educación")] = BusinessCategory.Services,
        [Comparable("Lavandería")] = BusinessCategory.Services
    };

    /// <summary>
    /// The category of the giro; for no giro, «Otro» or one written by hand, the one of the type: food to Comida,
    /// services to Servicios and a shop to Otros, since a shop can be of any category.
    /// </summary>
    public static BusinessCategory Suggest(string? industry, BusinessType businessType)
        => ByIndustry.TryGetValue(Comparable(industry), out var category)
            ? category
            : businessType switch
            {
                BusinessType.Food => BusinessCategory.Food,
                BusinessType.Service => BusinessCategory.Services,
                _ => BusinessCategory.Other
            };

    private static string Comparable(string? value)
    {
        var decomposed = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormD);
        var letters = decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);
        return new string(letters.ToArray()).ToLowerInvariant();
    }
}
