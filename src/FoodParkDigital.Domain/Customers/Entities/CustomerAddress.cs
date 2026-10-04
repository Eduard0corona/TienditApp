using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Customers.Entities;

/// <summary>A place a customer receives orders: "Casa", "Oficina"... with the references a delivery needs.</summary>
public class CustomerAddress : AuditableEntity, ITenantScoped
{
    public const int LabelMaxLength = 60;
    public const int AddressMaxLength = 300;
    public const int ReferencesMaxLength = 500;

    private CustomerAddress() { }

    internal CustomerAddress(Guid tenantId, Guid customerId, string label, string addressLine, string? references, bool isDefault)
    {
        TenantId = tenantId;
        CustomerId = customerId;
        IsDefault = isDefault;
        Change(label, addressLine, references);
    }

    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string Label { get; private set; } = null!;
    /// <summary>Street, number and neighborhood as the business writes them ("Av. Juárez 123, Col. Centro").</summary>
    public string AddressLine { get; private set; } = null!;
    /// <summary>How to find it: between streets, color of the house, who receives.</summary>
    public string? References { get; private set; }
    public bool IsDefault { get; private set; }

    internal void Change(string label, string addressLine, string? references)
    {
        var cleanLabel = label?.Trim();
        var cleanLine = addressLine?.Trim();
        if (string.IsNullOrWhiteSpace(cleanLine))
            throw new DomainException("Escribe la dirección.");
        if (cleanLine.Length > AddressMaxLength)
            throw new DomainException($"La dirección admite hasta {AddressMaxLength} caracteres.");
        var cleanReferences = string.IsNullOrWhiteSpace(references) ? null : references.Trim();
        if (cleanReferences is { Length: > ReferencesMaxLength })
            throw new DomainException($"Las referencias admiten hasta {ReferencesMaxLength} caracteres.");

        Label = string.IsNullOrWhiteSpace(cleanLabel) ? "Dirección" : cleanLabel.Length > LabelMaxLength ? cleanLabel[..LabelMaxLength] : cleanLabel;
        AddressLine = cleanLine;
        References = cleanReferences;
    }

    internal void MarkDefault(bool isDefault) => IsDefault = isDefault;
}
