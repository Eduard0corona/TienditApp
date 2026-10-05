namespace FoodParkDigital.Application.Common.Rules;

/// <summary>
/// The time zones a business in Mexico can pick (IANA ids, which .NET resolves on Linux and Windows): days, cuts and
/// reports start at the business's midnight, wherever the device that opens them is.
/// </summary>
public static class BusinessTimeZones
{
    public const string Default = "America/Mexico_City";

    public static readonly IReadOnlyList<string> Supported =
    [
        Default,              // Centro: CDMX, Jalisco, Nuevo León, Yucatán...
        "America/Cancun",     // Sureste: Quintana Roo
        "America/Mazatlan",   // Pacífico: Sinaloa, Sonora, Nayarit, Baja California Sur
        "America/Tijuana",    // Noroeste: Baja California (with summer time)
        "America/Matamoros"   // Frontera noreste (with summer time)
    ];

    public static bool IsSupported(string? timeZone) => timeZone is not null && Supported.Contains(timeZone);
}
