using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.Catalog.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>Archiving a category takes it out of the menus without leaving its products behind; restoring brings it back.</summary>
public class CategoryArchiveTests
{
    [Fact]
    public async Task ArchivingACategoryWithProducts_ShouldAskWhereToMoveThem()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        await fixture.AddProductAsync(tenant, "Taco", 25m);
        await fixture.AddProductAsync(tenant, "Agua", 15m);
        var general = await fixture.Context.Categories.SingleAsync();

        var result = await ToggleAsync(fixture, general.Id, isActive: false);

        Assert.Equal("La categoría tiene 2 productos: elige a qué categoría pasarlos antes de archivarla.", result.Error);
        Assert.True((await fixture.Context.Categories.AsNoTracking().SingleAsync()).IsActive);
    }

    [Fact]
    public async Task ArchivingWithATarget_ShouldMoveTheProductsAndHideTheCategory()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var taco = await fixture.AddProductAsync(tenant, "Taco", 25m);
        var general = await fixture.Context.Categories.SingleAsync();
        var drinks = await AddCategoryAsync(fixture, tenant, "Bebidas");

        var result = await ToggleAsync(fixture, general.Id, isActive: false, moveTo: drinks.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(drinks.Id, (await fixture.Context.Products.AsNoTracking().SingleAsync(p => p.Id == taco.Id)).CategoryId);
        var menu = await GetCategoriesAsync(fixture, includeArchived: false);
        Assert.Equal(["Bebidas"], menu.Select(c => c.Name));
        var all = await GetCategoriesAsync(fixture, includeArchived: true);
        Assert.Equal(["Bebidas", "General"], all.Select(c => c.Name));
        Assert.Equal([1, 0], all.Select(c => c.ProductCount));
        Assert.False(all.Single(c => c.Name == "General").IsActive);
    }

    [Fact]
    public async Task RestoringACategory_ShouldBringItBackToTheMenus()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var seasonal = await AddCategoryAsync(fixture, tenant, "Temporada");
        Assert.True((await ToggleAsync(fixture, seasonal.Id, isActive: false)).IsSuccess);
        Assert.Empty(await GetCategoriesAsync(fixture, includeArchived: false));

        var result = await ToggleAsync(fixture, seasonal.Id, isActive: true);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Temporada", Assert.Single(await GetCategoriesAsync(fixture, includeArchived: false)).Name);
    }

    [Fact]
    public async Task TargetFromAnotherBusinessOrArchived_ShouldBeRejected()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "otro");
        var foreign = await AddCategoryAsync(fixture, other, "Ajena");
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "propio");
        var taco = await fixture.AddProductAsync(tenant, "Taco", 25m);
        var general = await fixture.Context.Categories.SingleAsync();
        var archived = await AddCategoryAsync(fixture, tenant, "Vieja");
        archived.Deactivate();
        await fixture.Context.SaveChangesAsync();

        var toForeign = await ToggleAsync(fixture, general.Id, isActive: false, moveTo: foreign.Id);
        var toArchived = await ToggleAsync(fixture, general.Id, isActive: false, moveTo: archived.Id);
        var toItself = await ToggleAsync(fixture, general.Id, isActive: false, moveTo: general.Id);

        Assert.All([toForeign, toArchived, toItself], r => Assert.Equal("Elige otra categoría activa para los productos.", r.Error));
        Assert.Equal(general.Id, (await fixture.Context.Products.AsNoTracking().SingleAsync(p => p.Id == taco.Id)).CategoryId);
        Assert.True((await fixture.Context.Categories.AsNoTracking().SingleAsync(c => c.Id == general.Id)).IsActive);
    }

    private static async Task<Category> AddCategoryAsync(SalesTestFixture fixture, Tenant tenant, string name)
    {
        var category = new Category(tenant.Id, name, null);
        fixture.Context.Categories.Add(category);
        await fixture.Context.SaveChangesAsync();
        return category;
    }

    private static async Task<Application.Common.Models.Result> ToggleAsync(SalesTestFixture fixture, Guid categoryId, bool isActive, Guid? moveTo = null)
    {
        var result = await new ToggleCategoryActiveCommandHandler(fixture.Context).Handle(
            new ToggleCategoryActiveCommand { Id = categoryId, IsActive = isActive, MoveProductsToCategoryId = moveTo },
            CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static Task<List<Application.Catalog.DTOs.CategoryDto>> GetCategoriesAsync(SalesTestFixture fixture, bool includeArchived)
        => new GetCategoriesQueryHandler(fixture.Context, fixture.TenantService)
            .Handle(new GetCategoriesQuery { IncludeArchived = includeArchived }, CancellationToken.None);
}
