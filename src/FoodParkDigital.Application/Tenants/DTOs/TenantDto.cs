using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tenants.DTOs;

public class TenantDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public BusinessType BusinessType { get; set; }
    public TenantStatus Status { get; set; }
    public string ContactEmail { get; set; } = null!;
    public string? ContactPhone { get; set; }
    public string? LogoUrl { get; set; }
    public TenantCapability Capabilities { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
