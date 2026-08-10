using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tests;

public class TenantCapabilityDefaultsTests
{
    [Fact]
    public void FromBusinessType_Service_ShouldIncludeRetailCapability()
    {
        var capabilities = TenantCapabilityDefaults.FromBusinessType(BusinessType.Service);

        Assert.True((capabilities & TenantCapability.Service) == TenantCapability.Service);
        Assert.True((capabilities & TenantCapability.Retail) == TenantCapability.Retail);
        Assert.True((capabilities & TenantCapability.Inventory) == TenantCapability.Inventory);
    }
}
