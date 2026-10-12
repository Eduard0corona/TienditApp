using FoodParkDigital.Domain.Branches.Entities;
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
        var line = Join(", ",
            Join(" ", address.Street, address.ExteriorNumber, interior),
            address.Neighborhood,
            Join(" ", address.ZipCode, address.City),
            address.State,
            Abroad(address.Country));
        return line.Length == 0 ? null : line;
    }

    /// <summary>
    /// The address of a branch for its customers: none for a branch that only delivers, whose address (often a home)
    /// stays with the team.
    /// </summary>
    public static string? ForCustomers(Branch? branch) => branch is { KeepsAddressPrivate: true } ? null : OneLine(branch);

    /// <summary>
    /// Where a branch is, in one line and in the same order: "Av. Juárez 123, Centro, 72000 Puebla, Puebla". Null
    /// without a street: a city alone does not tell anyone how to get there. Private: a branch's address reaches
    /// customers only through <see cref="ForCustomers"/>.
    /// </summary>
    private static string? OneLine(Branch? branch)
    {
        if (branch is null || string.IsNullOrWhiteSpace(branch.AddressLine1))
            return null;

        return Join(", ",
            branch.AddressLine1,
            branch.AddressLine2,
            Join(" ", branch.PostalCode, branch.City),
            branch.State,
            Abroad(branch.Country));
    }

    private static string? Abroad(string? country) => country?.Trim() is "México" or "Mexico" or "" ? null : country;

    private static string Join(string separator, params string?[] parts)
        => string.Join(separator, parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));
}
