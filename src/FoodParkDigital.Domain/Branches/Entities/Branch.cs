using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Branches.Entities;

/// <summary>
/// A place where the business sells: sales, drawers and stock locations belong to a branch. Every business has
/// one default branch, which takes the sales and drawers that do not name another one and cannot be deactivated.
/// </summary>
public class Branch : AuditableEntity, ITenantScoped, IAggregateRoot
{
    public const int MaxBranchesPerTenant = 50;
    public const int MaxNameLength = 200;
    public const int MaxCodeLength = 50;

    private Branch() { }

    public Branch(Guid tenantId, string name, string code, BranchContact? contact = null, bool isDefault = false)
    {
        TenantId = tenantId;
        IsActive = true;
        IsDefault = isDefault;
        Update(name, code, contact ?? BranchContact.Empty);
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    /// <summary>Short unique code of the branch in the business (upper case), printed on reports.</summary>
    public string Code { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? AddressLine1 { get; private set; }
    public string? AddressLine2 { get; private set; }
    public string? City { get; private set; }
    public string? State { get; private set; }
    public string? Country { get; private set; }
    public string? PostalCode { get; private set; }
    public string? ContactName { get; private set; }
    public string? ContactPhone { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }

    public const string DefaultBranchName = "Sucursal principal";
    public const string DefaultBranchCode = "PRINCIPAL";

    /// <summary>The branch every business starts with.</summary>
    public static Branch CreateDefault(Guid tenantId) => new(tenantId, DefaultBranchName, DefaultBranchCode, isDefault: true);

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public void Update(string name, string code, BranchContact contact)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName) || trimmedName.Length > MaxNameLength)
            throw new DomainException($"El nombre de la sucursal es obligatorio y tiene máximo {MaxNameLength} caracteres.");
        var normalizedCode = code is null ? string.Empty : NormalizeCode(code);
        if (normalizedCode.Length == 0 || normalizedCode.Length > MaxCodeLength)
            throw new DomainException($"La clave de la sucursal es obligatoria y tiene máximo {MaxCodeLength} caracteres.");

        Name = trimmedName;
        Code = normalizedCode;
        Description = contact.Description;
        AddressLine1 = contact.AddressLine1;
        AddressLine2 = contact.AddressLine2;
        City = contact.City;
        State = contact.State;
        Country = contact.Country;
        PostalCode = contact.PostalCode;
        ContactName = contact.ContactName;
        ContactPhone = contact.ContactPhone;
    }

    /// <summary>Makes this the branch of sales and drawers that name none. The caller unmarks the previous one.</summary>
    public void MarkAsDefault()
    {
        if (!IsActive)
            throw new DomainException("Activa la sucursal antes de hacerla la principal.");
        IsDefault = true;
    }

    public void UnmarkAsDefault() => IsDefault = false;

    public void Activate() => IsActive = true;

    public void Deactivate()
    {
        if (IsDefault)
            throw new DomainException("No puedes desactivar la sucursal principal. Elige otra como principal primero.");
        IsActive = false;
    }
}

/// <summary>Address and contact of a branch; blank values are stored as empty.</summary>
public sealed record BranchContact(
    string? Description = null,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? City = null,
    string? State = null,
    string? Country = null,
    string? PostalCode = null,
    string? ContactName = null,
    string? ContactPhone = null)
{
    public static BranchContact Empty { get; } = new();

    public string? Description { get; } = Clean(Description);
    public string? AddressLine1 { get; } = Clean(AddressLine1);
    public string? AddressLine2 { get; } = Clean(AddressLine2);
    public string? City { get; } = Clean(City);
    public string? State { get; } = Clean(State);
    public string? Country { get; } = Clean(Country);
    public string? PostalCode { get; } = Clean(PostalCode);
    public string? ContactName { get; } = Clean(ContactName);
    public string? ContactPhone { get; } = Clean(ContactPhone);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
