using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Orders.HomeDelivery;

/// <summary>
/// Where an order goes (Fase 22C): the postal code its delivery zone is found by, the address as the customer writes it
/// (street, number and neighborhood in one line, as a business's customer addresses keep it) and, optionally, references
/// to find the place. Kept trimmed; the postal code with the rules of the zones.
/// </summary>
public sealed record DeliveryAddress
{
    public const int MaxAddressLineLength = 300;
    public const int MaxReferencesLength = 500;

    public const string PostalCodeRequiredMessage = "Escribe el código postal de la entrega.";
    public const string AddressLineRequiredMessage = "Escribe a dónde va el pedido: calle, número y colonia.";
    public const string AddressLineTooLongMessage = "La dirección admite hasta 300 caracteres.";
    public const string ReferencesTooLongMessage = "Las referencias admiten hasta 500 caracteres.";

    // The same address for a visit at the customer's (Fase 22E), said for it.
    public const string VisitPostalCodeRequiredMessage = "Escribe el código postal de la visita.";
    public const string VisitAddressLineRequiredMessage = "Escribe a dónde es la visita: calle, número y colonia.";

    private DeliveryAddress(string postalCode, string addressLine, string? references)
    {
        PostalCode = postalCode;
        AddressLine = addressLine;
        References = references;
    }

    public string PostalCode { get; }
    public string AddressLine { get; }
    /// <summary>How to find the place («casa azul, portón negro»), or none.</summary>
    public string? References { get; }

    /// <summary>The address as written, trimmed, or a <see cref="DomainException"/> saying what is missing or wrong.</summary>
    public static DeliveryAddress From(string? postalCode, string? addressLine, string? references)
        => Read(postalCode, addressLine, references, PostalCodeRequiredMessage, AddressLineRequiredMessage);

    /// <summary>The address of a visit at the customer's (Fase 22E): the same rules, said for a visit.</summary>
    public static DeliveryAddress ForVisit(string? postalCode, string? addressLine, string? references)
        => Read(postalCode, addressLine, references, VisitPostalCodeRequiredMessage, VisitAddressLineRequiredMessage);

    private static DeliveryAddress Read(
        string? postalCode, string? addressLine, string? references, string postalCodeRequired, string addressLineRequired)
    {
        var code = DeliveryZone.NormalizePostalCode(postalCode)
            ?? throw new DomainException(postalCodeRequired);
        if (!DeliveryZone.IsPostalCode(code))
            throw new DomainException(DeliveryZone.NotAPostalCodeMessage(code));

        var line = addressLine?.Trim();
        if (string.IsNullOrEmpty(line))
            throw new DomainException(addressLineRequired);
        if (line.Length > MaxAddressLineLength)
            throw new DomainException(AddressLineTooLongMessage);

        var notes = string.IsNullOrWhiteSpace(references) ? null : references.Trim();
        if (notes is { Length: > MaxReferencesLength })
            throw new DomainException(ReferencesTooLongMessage);

        return new DeliveryAddress(code, line, notes);
    }
}
