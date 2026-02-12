using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tenants.Entities;

public class Tenant : AuditableEntity
{
    private Tenant() { }

    public Tenant(string name, string slug, BusinessType businessType, string contactEmail)
    {
        Name = name;
        Slug = slug;
        BusinessType = businessType;
        ContactEmail = contactEmail;
        Status = TenantStatus.Active;
    }

    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public BusinessType BusinessType { get; private set; }
    public TenantStatus Status { get; private set; }
    public string ContactEmail { get; private set; } = null!;
    public string? ContactPhone { get; private set; }
    public string? LogoUrl { get; private set; }

    public TenantSettings? Settings { get; private set; }

    public void Update(string name, string contactEmail, string? contactPhone, string? logoUrl)
    {
        Name = name;
        ContactEmail = contactEmail;
        ContactPhone = contactPhone;
        LogoUrl = logoUrl;
    }

    public void SetStatus(TenantStatus status) => Status = status;
}
