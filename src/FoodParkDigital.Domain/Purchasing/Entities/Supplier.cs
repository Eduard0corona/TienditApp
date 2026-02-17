using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Purchasing.Entities;

public class Supplier : AuditableEntity, ITenantScoped
{
    private Supplier() { }

    public Supplier(
        Guid tenantId,
        string name,
        string? contactName = null,
        string? phone = null,
        string? email = null,
        string? taxId = null,
        string? address = null,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del proveedor es obligatorio.");

        TenantId = tenantId;
        Name = name.Trim();
        ContactName = contactName?.Trim();
        Phone = phone?.Trim();
        Email = email?.Trim();
        TaxId = taxId?.Trim();
        Address = address?.Trim();
        Notes = notes?.Trim();
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? ContactName { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? TaxId { get; private set; }
    public string? Address { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(
        string name,
        string? contactName,
        string? phone,
        string? email,
        string? taxId,
        string? address,
        string? notes)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("El nombre del proveedor es obligatorio.");

        Name = name.Trim();
        ContactName = contactName?.Trim();
        Phone = phone?.Trim();
        Email = email?.Trim();
        TaxId = taxId?.Trim();
        Address = address?.Trim();
        Notes = notes?.Trim();
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }
}
