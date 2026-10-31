using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.Catalog.Queries;
using FoodParkDigital.Application.Catalog.Templates;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>The category templates of Catálogo: the one recommended by giro or type, and what installing one creates.</summary>
public class CategoryTemplatesTests
{
    [Theory]
    [InlineData("Consultorio", "medical")]
    [InlineData("Consultorio dental", "dental")]
    [InlineData("Carpinteria", "carpentry")]
    [InlineData("HERRERÍA Y SOLDADURA", "welding")]
    [InlineData("Electricista", "electrical")]
    [InlineData("Plomería", "plumbing")]
    [InlineData("Aire acondicionado", "air-conditioning")]
    [InlineData("Fumigación", "pest-control")]
    [InlineData("Refrigeración y línea blanca", "refrigeration")]
    [InlineData("cerrajeria", "locksmith")]
    [InlineData("Jardinería", "gardening")]
    [InlineData("Limpieza del hogar", "home-cleaning")]
    [InlineData("Pintura e impermeabilización", "painting")]
    // The older ones keep finding a word of the giro, or the type.
    [InlineData("Lavandería", "laundry")]
    [InlineData("Barbería Don Pepe", "barber")]
    [InlineData("Servicios profesionales", "services")]
    [InlineData(null, "services")]
    public async Task GetTemplates_ShouldRecommendTheOneOfTheGiro(string? industry, string expected)
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service);
        tenant.SetBusinessInfo(null, null, industry, null, null);
        await fixture.Context.SaveChangesAsync();
        var query = new GetCategoryTemplatesQueryHandler(fixture.Context, fixture.TenantService);

        var templates = await query.Handle(new GetCategoryTemplatesQuery(), CancellationToken.None);

        var recommended = Assert.Single(templates, template => template.IsRecommended);
        Assert.Equal(expected, recommended.Id);
    }

    [Theory]
    [InlineData("medical")]
    [InlineData("dental")]
    [InlineData("carpentry")]
    [InlineData("welding")]
    [InlineData("electrical")]
    [InlineData("plumbing")]
    [InlineData("air-conditioning")]
    public async Task Install_ShouldCreateEveryCategoryOfAServiceGiro(string templateId)
    {
        await using var fixture = SalesTestFixture.Create();
        await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service);
        var handler = new InstallCategoryTemplateCommandHandler(fixture.Context, fixture.TenantService);
        var template = CategoryTemplateCatalog.GetById(templateId)!;

        var result = await handler.Handle(new InstallCategoryTemplateCommand { TemplateId = templateId }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(BusinessType.Service, template.BusinessType);
        Assert.Equal(template.Categories.Count, result.Value.CreatedCount);
        Assert.Equal(
            template.Categories.Select(category => category.Name).Order(),
            (await fixture.Context.Categories.Select(category => category.Name).ToListAsync()).Order());
    }

    [Fact]
    public void Templates_ShouldEachHaveTheirOwnId()
    {
        var ids = CategoryTemplateCatalog.GetAll().Select(template => template.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
