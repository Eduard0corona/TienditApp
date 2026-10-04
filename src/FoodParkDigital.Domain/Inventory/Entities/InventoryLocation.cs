using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Inventory.Entities;

public class InventoryLocation : AuditableEntity, ITenantScoped
{
    private InventoryLocation() { }

    public InventoryLocation(
        Guid tenantId,
        string name,
        string code,
        string? description = null,
        string? addressLine1 = null,
        string? addressLine2 = null,
        string? city = null,
        string? state = null,
        string? country = null,
        string? postalCode = null,
        string? contactName = null,
        string? contactPhone = null,
        bool isDefault = false,
        Guid? branchId = null)
    {
        TenantId = tenantId;
        BranchId = branchId;
        Name = name;
        Code = code;
        Description = description;
        AddressLine1 = addressLine1;
        AddressLine2 = addressLine2;
        City = city;
        State = state;
        Country = country;
        PostalCode = postalCode;
        ContactName = contactName;
        ContactPhone = contactPhone;
        IsDefault = isDefault;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
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

    /// <summary>Branch the stock belongs to; none for a warehouse that serves every branch.</summary>
    public Guid? BranchId { get; private set; }

    public void Update(
        string name,
        string code,
        string? description = null,
        string? addressLine1 = null,
        string? addressLine2 = null,
        string? city = null,
        string? state = null,
        string? country = null,
        string? postalCode = null,
        string? contactName = null,
        string? contactPhone = null)
    {
        Name = name;
        Code = code;
        Description = description;
        AddressLine1 = addressLine1;
        AddressLine2 = addressLine2;
        City = city;
        State = state;
        Country = country;
        PostalCode = postalCode;
        ContactName = contactName;
        ContactPhone = contactPhone;
    }

    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    public void AssignToBranch(Guid? branchId) => BranchId = branchId;

    public void SetActive(bool isActive) => IsActive = isActive;
}
