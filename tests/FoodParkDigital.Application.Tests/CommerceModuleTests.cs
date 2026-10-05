using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// A business always sells something: products, services or food. Like its plan, its own modules keep a type of
/// sale, so catalog, customers and orders never depend on one being left on.
/// </summary>
public class CommerceModuleTests
{
    private const string NoTypeOfSale = "Elige al menos un tipo de venta: productos, servicios o alimentos.";

    [Theory]
    [InlineData(BusinessType.Food, TenantCapability.Appointments, TenantCapability.Appointments | TenantCapability.Food)]
    [InlineData(
        BusinessType.Service,
        TenantCapability.Inventory | TenantCapability.Kitchen,
        TenantCapability.Inventory | TenantCapability.Kitchen | TenantCapability.Service | TenantCapability.Retail)]
    [InlineData(BusinessType.Retail, TenantCapability.Food | TenantCapability.Inventory, TenantCapability.Food | TenantCapability.Inventory)]
    public void ModulesWithoutATypeOfSale_ShouldKeepTheOneOfTheirBusinessType(
        BusinessType businessType, TenantCapability configured, TenantCapability expected)
    {
        Assert.Equal(expected, TenantCapabilityRules.NormalizeCapabilities(configured, businessType));
    }

    [Fact]
    public void BusinessSettings_ShouldRefuseModulesWithoutATypeOfSale()
    {
        var validator = new UpdateMyTenantCommandValidator();

        var withoutSale = validator.Validate(Settings(TenantCapability.Inventory | TenantCapability.Appointments));
        var withSale = validator.Validate(Settings(TenantCapability.Service | TenantCapability.Appointments));
        var unchanged = validator.Validate(Settings(null));

        Assert.Contains(withoutSale.Errors, e => e.ErrorMessage == NoTypeOfSale);
        Assert.True(withSale.IsValid, string.Join(", ", withSale.Errors));
        Assert.True(unchanged.IsValid, string.Join(", ", unchanged.Errors));
    }

    private static UpdateMyTenantCommand Settings(TenantCapability? capabilities) => new()
    {
        Name = "Tienda",
        ContactEmail = "tienda@demo.com",
        Capabilities = capabilities
    };
}
