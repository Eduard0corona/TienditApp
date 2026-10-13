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
    {
        var code = DeliveryZone.NormalizePostalCode(postalCode)
            ?? throw new DomainException(PostalCodeRequiredMessage);
        if (!DeliveryZone.IsPostalCode(code))
            throw new DomainException(DeliveryZone.NotAPostalCodeMessage(code));

        var line = addressLine?.Trim();
        if (string.IsNullOrEmpty(line))
            throw new DomainException(AddressLineRequiredMessage);
        if (line.Length > MaxAddressLineLength)
            throw new DomainException(AddressLineTooLongMessage);

        var notes = string.IsNullOrWhiteSpace(references) ? null : references.Trim();
        if (notes is { Length: > MaxReferencesLength })
            throw new DomainException(ReferencesTooLongMessage);

        return new DeliveryAddress(code, line, notes);
    }
}
