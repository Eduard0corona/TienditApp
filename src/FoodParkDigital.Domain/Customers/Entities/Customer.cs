using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Customers.Entities;

public class Customer : AuditableEntity, ITenantScoped
{
    private Customer() { }

    public Customer(
        Guid tenantId,
        string name,
        string? phone,
        string? email,
        bool isPublicGeneral = false)
    {
        TenantId = tenantId;
        Name = name;
        Phone = phone;
        Email = email;
        IsPublicGeneral = isPublicGeneral;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public bool IsPublicGeneral { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, string? phone, string? email)
    {
        Name = name;
        Phone = phone;
        Email = email;
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
    }
}

