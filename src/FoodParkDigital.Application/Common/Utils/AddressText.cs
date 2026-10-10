using FoodParkDigital.Domain.Common.ValueObjects;

namespace FoodParkDigital.Application.Common.Utils;

public static class AddressText
{
    /// <summary>
    /// The address in one line, as it is written in Mexico and searched in a map: "Av. Juárez 123 int. 4, Centro,
    /// 72000 Puebla, Puebla". Parts left blank are skipped, and the country only shows when it is not Mexico.
    /// </summary>
    public static string? OneLine(Address? address)
    {
        if (address is null)
            return null;

        var interior = string.IsNullOrWhiteSpace(address.InteriorNumber) ? null : $"int. {address.InteriorNumber.Trim()}";
        var country = address.Country?.Trim() is "México" or "Mexico" or "" ? null : address.Country;
        var line = Join(", ",
            Join(" ", address.Street, address.ExteriorNumber, interior),
            address.Neighborhood,
            Join(" ", address.ZipCode, address.City),
            address.State,
            country);
        return line.Length == 0 ? null : line;
    }

    private static string Join(string separator, params string?[] parts)
        => string.Join(separator, parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));
}
