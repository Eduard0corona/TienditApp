using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Application.Tenants.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>Guided setup: sales policy, starter catalogs and the first-steps checklist.</summary>
public class TenantSetupTests
{
    [Fact]
    public async Task UpdateSalesPolicy_ShouldChangeFutureSalesAndRecordWhoChangedIt()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: false);
        var handler = new UpdateSalesPolicyCommandHandler(fixture.Context, fixture.CurrentUser, fixture.TenantService);

        var result = await handler.Handle(
            new UpdateSalesPolicyCommand { PricesIncludeTax = true, DefaultTaxRate = 8m, AutoCloseCounterSales = false },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var settings = await fixture.Context.TenantSettings.SingleAsync(x => x.TenantId == tenant.Id);
        Assert.True(settings.PricesIncludeTax);
        Assert.Equal(8m, settings.DefaultTaxRate);
        Assert.False(settings.AutoCloseCounterSales);
        var audit = await fixture.Context.AuditLogs.SingleAsync();
        Assert.Equal("TenantSalesPolicyUpdated", audit.Action);
        Assert.Equal(tenant.Id, audit.TenantId);
        Assert.Contains("\"PricesIncludeTax\":false", audit.Metadata);
    }

    [Theory]
    [InlineData(16, true)]
    [InlineData(8, true)]
    [InlineData(0, true)]
    [InlineData(11, false)]
    [InlineData(0.16, false)]
    public void UpdateSalesPolicy_ShouldOnlyAcceptIvaRates(double rate, bool valid)
    {
        var result = new UpdateSalesPolicyCommandValidator().Validate(
            new UpdateSalesPolicyCommand { DefaultTaxRate = (decimal)rate });

        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public async Task InstallStarterCatalog_ShouldCreateCategoriesAndProductsWithTheirRates()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var handler = new InstallStarterCatalogCommandHandler(fixture.Context, fixture.TenantService);

        var result = await handler.Handle(new InstallStarterCatalogCommand { TemplateId = "abarrotes" }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(3, result.Value.CategoriesCreated);
        Assert.Equal(12, result.Value.ProductsCreated);
        var tortilla = await fixture.Context.Products.SingleAsync(p => p.Name == "Tortilla de maíz 1 kg");
        var soap = await fixture.Context.Products.SingleAsync(p => p.Name == "Jabón de tocador");
        Assert.Equal(0m, tortilla.TaxRate);
        Assert.Null(soap.TaxRate);
        Assert.Equal("MXN", soap.PriceCurrency);
    }

    [Fact]
    public async Task InstallStarterCatalog_ForAKitchenThatOnlyDelivers_ShouldCreateItsMenuWithTheBusinessRate()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        var handler = new InstallStarterCatalogCommandHandler(fixture.Context, fixture.TenantService);

        var result = await handler.Handle(new InstallStarterCatalogCommand { TemplateId = "comida-a-domicilio" }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(5, result.Value.CategoriesCreated);
        Assert.Equal(10, result.Value.ProductsCreated);
        // Prepared food pays the business's IVA, delivered or not.
        Assert.All(await fixture.Context.Products.ToListAsync(), product => Assert.Null(product.TaxRate));
        Assert.True((await fixture.Context.Products.SingleAsync(p => p.Name == "Paquete familiar")).IsFeatured);
    }

    [Fact]
    public async Task InstallStarterCatalog_ShouldNeverTouchACatalogWithProducts()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        await fixture.AddProductAsync(tenant, "Producto propio", 10m);
        var handler = new InstallStarterCatalogCommandHandler(fixture.Context, fixture.TenantService);

        var result = await handler.Handle(new InstallStarterCatalogCommand { TemplateId = "abarrotes" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Single(await fixture.Context.Products.ToListAsync());
    }

    [Fact]
    public async Task InstallStarterCatalog_ShouldRequireTheModuleOfItsProducts()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
        var handler = new InstallStarterCatalogCommandHandler(fixture.Context, fixture.TenantService);

        var result = await handler.Handle(new InstallStarterCatalogCommand { TemplateId = "lavanderia" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(await fixture.Context.Products.ToListAsync());
    }

    [Fact]
    public async Task InstallStarterCatalog_ShouldCreateTrackableServicesForAServiceBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service, pricesIncludeTax: true);
        var handler = new InstallStarterCatalogCommandHandler(fixture.Context, fixture.TenantService);

        var result = await handler.Handle(new InstallStarterCatalogCommand { TemplateId = "lavanderia" }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var services = await fixture.Context.Products.ToListAsync();
        Assert.NotEmpty(services);
        Assert.All(services, s => Assert.Equal(UnitType.Service, s.Unit));
        Assert.All(services, s => Assert.True(s.IsTrackable));
    }

    [Fact]
    public async Task Setup_ShouldReportTheFirstStepsAndRememberWhenItWasFinished()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true);
        var query = new GetTenantSetupQueryHandler(fixture.Context, fixture.CurrentUser, fixture.TenantService);

        var before = await query.Handle(new GetTenantSetupQuery(), CancellationToken.None);
        await fixture.AddProductAsync(tenant, "Americano", 35m);
        await fixture.OpenCashSessionAsync(tenant);
        var complete = await new CompleteTenantSetupCommandHandler(
                fixture.Context, fixture.CurrentUser, fixture.TenantService, fixture.Clock)
            .Handle(new CompleteTenantSetupCommand(), CancellationToken.None);
        var after = await query.Handle(new GetTenantSetupQuery(), CancellationToken.None);

        Assert.True(before.IsSuccess, before.Error);
        Assert.Null(before.Value.CompletedAt);
        Assert.False(before.Value.HasProducts);
        Assert.All(before.Value.Steps, step => Assert.False(step.Done));
        Assert.Contains(before.Value.StarterCatalogs, c => c.Id == "cafeteria" && c.BusinessType == BusinessType.Food);

        Assert.True(complete.IsSuccess, complete.Error);
        Assert.Equal(SalesTestFixture.Now, after.Value.CompletedAt);
        var done = after.Value.Steps.Where(step => step.Done).Select(step => step.Key).ToList();
        Assert.Equal(["salesPolicy", "catalog", "cash"], done);
    }
}
