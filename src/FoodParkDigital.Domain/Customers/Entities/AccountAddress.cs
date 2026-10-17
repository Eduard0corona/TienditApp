using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.HomeDelivery;

namespace FoodParkDigital.Domain.Customers.Entities;

/// <summary>
/// An address a customer account keeps for its orders (Fase 24B): «Casa», «Oficina»… with the postal code the delivery
/// zones of each business are checked by, the address and its references, kept with the rules of an order's address
/// (<see cref="DeliveryAddress"/>). It belongs to the account, not to a business: an order copies it, and a business
/// sees it only in the orders sent there.
/// </summary>
public class AccountAddress : AuditableEntity, IAggregateRoot
{
    /// <summary>The addresses one account keeps.</summary>
    public const int MaxPerAccount = 10;
    public const int MaxLabelLength = 60;
    public const string DefaultLabel = "Dirección";

    public const string AccountRequiredMessage = "La dirección necesita una cuenta.";
    public const string TooManyMessage = "Guardas hasta 10 direcciones: borra una para agregar otra.";

    private AccountAddress() { }

    private AccountAddress(Guid userId, string label, DeliveryAddress address)
    {
        UserId = userId;
        Label = label;
        Apply(address);
    }

    public Guid UserId { get; private set; }

    /// <summary>How the customer calls it: «Casa», «Oficina»; <see cref="DefaultLabel"/> without one.</summary>
    public string Label { get; private set; } = DefaultLabel;

    public string PostalCode { get; private set; } = string.Empty;

    /// <summary>Street, number and neighborhood in one line, as an order keeps it.</summary>
    public string AddressLine { get; private set; } = string.Empty;

    /// <summary>How to find the place («casa azul, portón negro»), or none.</summary>
    public string? References { get; private set; }

    /// <summary>The last time an order of the account went here; the store offers it first. Null until then.</summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>
    /// A new address of the account, with the rules of an order's address (a <see cref="DomainException"/> says what is
    /// missing or wrong), while the account keeps fewer than <see cref="MaxPerAccount"/>.
    /// </summary>
    /// <param name="addressesKept">How many addresses the account keeps now.</param>
    public static AccountAddress Create(
        Guid userId, string? label, string? postalCode, string? addressLine, string? references, int addressesKept)
    {
        if (userId == Guid.Empty)
            throw new DomainException(AccountRequiredMessage);
        if (addressesKept >= MaxPerAccount)
            throw new DomainException(TooManyMessage);

        return new AccountAddress(userId, NormalizeLabel(label), DeliveryAddress.From(postalCode, addressLine, references));
    }

    /// <summary>Changes the name and the address, with the same rules as a new one.</summary>
    public void Change(string? label, string? postalCode, string? addressLine, string? references)
    {
        var address = DeliveryAddress.From(postalCode, addressLine, references);
        Label = NormalizeLabel(label);
        Apply(address);
    }

    /// <summary>
    /// The same place as <paramref name="address"/>: the same postal code and the same line, whatever its capitals and
    /// spaces. Its references may differ.
    /// </summary>
    public bool IsSamePlaceAs(DeliveryAddress address)
        => PostalCode == address.PostalCode && Comparable(AddressLine) == Comparable(address.AddressLine);

    /// <summary>An order of the account went here: it is offered first. Its references are the ones that order used.</summary>
    public void MarkUsed(DeliveryAddress address, DateTimeOffset at)
    {
        if (!IsSamePlaceAs(address))
            throw new DomainException("La dirección del pedido no es esta.");
        References = address.References;
        LastUsedAt = at;
    }

    /// <summary>The address as an order takes it.</summary>
    public DeliveryAddress ToDeliveryAddress() => DeliveryAddress.From(PostalCode, AddressLine, References);

    private void Apply(DeliveryAddress address)
    {
        PostalCode = address.PostalCode;
        AddressLine = address.AddressLine;
        References = address.References;
    }

    /// <summary>On one line (a chip of the store shows it), with single spaces and at most <see cref="MaxLabelLength"/>.</summary>
    private static string NormalizeLabel(string? label)
    {
        var clean = SingleSpaced(new string((label ?? string.Empty).Select(c => char.IsControl(c) ? ' ' : c).ToArray()));
        if (clean.Length == 0)
            return DefaultLabel;
        return clean.Length > MaxLabelLength ? clean[..MaxLabelLength].TrimEnd() : clean;
    }

    private static string Comparable(string line) => SingleSpaced(line).ToUpperInvariant();

    private static string SingleSpaced(string text)
        => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
