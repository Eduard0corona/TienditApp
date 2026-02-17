namespace FoodParkDigital.Domain.Tenants.Enums;

public static class TenantCapabilityDefaults
{
    public static TenantCapability FromBusinessType(BusinessType businessType)
        => businessType switch
        {
            BusinessType.Food => TenantCapability.Food | TenantCapability.Inventory | TenantCapability.Kitchen,
            BusinessType.Retail => TenantCapability.Retail | TenantCapability.Inventory,
            BusinessType.Service => TenantCapability.Service | TenantCapability.Inventory | TenantCapability.Appointments,
            _ => TenantCapability.Inventory
        };
}
