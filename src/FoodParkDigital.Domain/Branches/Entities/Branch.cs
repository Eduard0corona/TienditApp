using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Common.ValueObjects;
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

    public Branch(
        Guid tenantId,
        string name,
        string code,
        BranchContact? contact = null,
        bool isDefault = false,
        BranchServiceMode serviceMode = BranchServiceMode.Premises)
    {
        TenantId = tenantId;
        IsActive = true;
        IsDefault = isDefault;
        Update(name, code, contact ?? BranchContact.Empty, serviceMode);
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

    /// <summary>Whether customers come to it or it only delivers; a branch that only delivers keeps its address private.</summary>
    public BranchServiceMode ServiceMode { get; private set; }

    /// <summary>Delivery only: its address is the team's, never the customers'.</summary>
    public bool KeepsAddressPrivate => ServiceMode == BranchServiceMode.DeliveryOnly;

    /// <summary>Its latitude on the map (Fase 24G), with <see cref="Longitude"/>; both or neither.</summary>
    public decimal? Latitude { get; private set; }
    public decimal? Longitude { get; private set; }

    /// <summary>Where it is on the map; none until its owner sets it.</summary>
    public GeoPoint? Location => GeoPoint.FromStored(Latitude, Longitude);

    /// <summary>
    /// The point customers may see (the distance in Explorar, «Cómo llegar»): the one of a branch they come to, never of
    /// one that only delivers. Kept when it changes how it serves, it shows again if it opens its premises.
    /// </summary>
    public GeoPoint? PublicLocation => KeepsAddressPrivate ? null : Location;

    public const string DefaultBranchName = "Sucursal principal";
    public const string DefaultBranchCode = "PRINCIPAL";

    public const string ServiceModeMessage = "Elige cómo atiende la sucursal.";

    /// <summary>The branch every business starts with: delivery only for a business that has no premises.</summary>
    public static Branch CreateDefault(Guid tenantId, BranchServiceMode serviceMode = BranchServiceMode.Premises)
        => new(tenantId, DefaultBranchName, DefaultBranchCode, isDefault: true, serviceMode: serviceMode);

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public void Update(string name, string code, BranchContact contact, BranchServiceMode serviceMode)
    {
        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName) || trimmedName.Length > MaxNameLength)
            throw new DomainException($"El nombre de la sucursal es obligatorio y tiene máximo {MaxNameLength} caracteres.");
        var normalizedCode = code is null ? string.Empty : NormalizeCode(code);
        if (normalizedCode.Length == 0 || normalizedCode.Length > MaxCodeLength)
            throw new DomainException($"La clave de la sucursal es obligatoria y tiene máximo {MaxCodeLength} caracteres.");
        if (!Enum.IsDefined(serviceMode))
            throw new DomainException(ServiceModeMessage);

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
        ServiceMode = serviceMode;
    }

    /// <summary>Puts the branch on the map, or takes it off with null.</summary>
    public void SetLocation(GeoPoint? location)
    {
        Latitude = location?.Latitude;
        Longitude = location?.Longitude;
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
