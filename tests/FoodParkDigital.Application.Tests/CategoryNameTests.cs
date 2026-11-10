using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Catalog.Templates;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// A name is in one category of a business at most, archived ones included, whatever its capitals and spaces (accents
/// count); another business can have it. Creating or editing with a name the business has is refused naming the category
/// that has it, also when another save takes it at the same moment.
/// </summary>
public class CategoryNameTests
{
    [Theory]
    [InlineData("Bebidas", "Bebidas")]
    [InlineData("Bebidas", "bebidas")]
    [InlineData("Bebidas", "  BEBIDAS  ")]
    [InlineData("Bebidas", "Bebidas\t")]
    [InlineData("Bebidas frías", "bebidas   frías")]
    [InlineData("Bebidas frías", "Bebidas Frías")]
    public async Task Creating_WithANameTheBusinessHas_ShouldBeRefused_NamingIt(string existing, string typed)
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        await AddCategoryAsync(fixture, tenant, existing);

        var result = await CreateAsync(fixture, typed);

        Assert.Equal($"Ya tienes una categoría llamada «{existing}». Ponle otro nombre.", result.Error);
        Assert.Equal([existing], await NamesAsync(fixture));
    }

    [Fact]
    public async Task Creating_WithTheNameOfAnArchivedOne_ShouldAskToRestoreIt()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        await AddCategoryAsync(fixture, tenant, "Temporada", archived: true);

        var result = await CreateAsync(fixture, "temporada");

        Assert.Equal("Ya tienes una categoría archivada llamada «Temporada». Restáurala o ponle otro nombre.", result.Error);
        Assert.Equal(["Temporada"], await NamesAsync(fixture));
    }

    [Fact]
    public async Task Creating_ANameOfAnotherBusiness_OrWithOtherAccents_ShouldWork()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "otro");
        await AddCategoryAsync(fixture, other, "Bebidas");
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: "propio");
        await AddCategoryAsync(fixture, tenant, "Cafe");

        var drinks = await CreateAsync(fixture, "Bebidas");
        var coffee = await CreateAsync(fixture, "Café");

        Assert.True(drinks.IsSuccess, drinks.Error);
        Assert.True(coffee.IsSuccess, coffee.Error);
        Assert.Equal(["Bebidas", "Cafe", "Café"], await NamesAsync(fixture));
        Assert.Equal(2, await fixture.Context.Categories.IgnoreQueryFilters().CountAsync(c => c.Name == "Bebidas"));
    }

    [Theory]
    [InlineData("Bebidas", false, "Ya tienes una categoría llamada «Bebidas». Ponle otro nombre.")]
    [InlineData(" BEBIDAS ", false, "Ya tienes una categoría llamada «Bebidas». Ponle otro nombre.")]
    [InlineData("temporada", true, "Ya tienes una categoría archivada llamada «Temporada». Restáurala o ponle otro nombre.")]
    public async Task Editing_ToANameAnotherCategoryHas_ShouldBeRefused_AndChangeNothing(string typed, bool archived, string error)
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        await AddCategoryAsync(fixture, tenant, archived ? "Temporada" : "Bebidas", archived);
        var desserts = await AddCategoryAsync(fixture, tenant, "Postres");

        var result = await UpdateAsync(fixture, desserts.Id, typed, "Dulces");

        Assert.Equal(error, result.Error);
        var stored = await fixture.Context.Categories.AsNoTracking().SingleAsync(c => c.Id == desserts.Id);
        Assert.Equal(("Postres", (string?)null), (stored.Name, stored.Description));
    }

    [Fact]
    public async Task Editing_ItsOwnNameInOtherCapitalsOrSpaces_ShouldWork()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var drinks = await AddCategoryAsync(fixture, tenant, "bebidas  frías");

        var result = await UpdateAsync(fixture, drinks.Id, "Bebidas frías", "Aguas y refrescos");

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(("Bebidas frías", "Aguas y refrescos"), (result.Value.Name, result.Value.Description));
    }

    [Fact]
    public async Task Editing_ACategoryRepeatedBeforeTheRule_ShouldWork_UntilItTakesANameThatIsTaken()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        await AddCategoryAsync(fixture, tenant, "Bebidas");
        var repeated = await AddCategoryAsync(fixture, tenant, "bebidas ");
        await AddCategoryAsync(fixture, tenant, "Postres");

        // Its description and order change without renaming it.
        var described = await UpdateAsync(fixture, repeated.Id, "bebidas ", "Las de antes", sortOrder: 5);
        Assert.True(described.IsSuccess, described.Error);
        Assert.Equal(("bebidas ", "Las de antes", 5), (described.Value.Name, described.Value.Description, described.Value.SortOrder));

        // A new name is checked: one of the business is refused, a free one is kept.
        var taken = await UpdateAsync(fixture, repeated.Id, "postres", null);
        Assert.Equal("Ya tienes una categoría llamada «Postres». Ponle otro nombre.", taken.Error);
        var renamed = await UpdateAsync(fixture, repeated.Id, "Bebidas de antes", null);
        Assert.True(renamed.IsSuccess, renamed.Error);
        Assert.Equal(["Bebidas", "Bebidas de antes", "Postres"], await NamesAsync(fixture));
    }

    [Fact]
    public async Task Creating_ANameSavedMeanwhile_ShouldBeRefused_InsteadOfFailing()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        database.SetTenant(tenantId);
        database.BeforeNextSave(() => SaveMeanwhileAsync(database, new Category(tenantId, "Bebidas", null)));
        await using var context = database.CreateContext();

        var result = await new CreateCategoryCommandHandler(context, database.TenantService)
            .Handle(new CreateCategoryCommand { Name = "Bebidas" }, CancellationToken.None);

        Assert.Equal("Ya tienes una categoría llamada «Bebidas». Ponle otro nombre.", result.Error);
        await using var check = database.CreateIndependentContext();
        Assert.Equal(["Bebidas"], await check.Categories.Select(c => c.Name).ToListAsync());
    }

    [Fact]
    public async Task Editing_ToANameSavedMeanwhile_ShouldBeRefused_InsteadOfFailing()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        database.SetTenant(tenantId);
        var desserts = new Category(tenantId, "Postres", null);
        await SaveMeanwhileAsync(database, desserts);
        database.BeforeNextSave(() => SaveMeanwhileAsync(database, new Category(tenantId, "Bebidas", null)));
        await using var context = database.CreateContext();

        var result = await new UpdateCategoryCommandHandler(context)
            .Handle(new UpdateCategoryCommand { Id = desserts.Id, Name = "Bebidas" }, CancellationToken.None);

        Assert.Equal("Ya tienes una categoría llamada «Bebidas». Ponle otro nombre.", result.Error);
        await using var check = database.CreateIndependentContext();
        Assert.Equal(["Bebidas", "Postres"], await check.Categories.OrderBy(c => c.Name).Select(c => c.Name).ToListAsync());
    }

    [Fact]
    public async Task Editing_ARepeatedOneToTheExactNameOfItsTwin_ShouldBeRefusedNamingTheTwin_InsteadOfFailing()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        database.SetTenant(tenantId);
        // Repeated before the rule, by a space at the end: the same key, two values for the index.
        var twin = new Category(tenantId, "Bebidas", null);
        twin.Deactivate();
        await SaveMeanwhileAsync(database, twin);
        var repeated = new Category(tenantId, "Bebidas ", null);
        await SaveMeanwhileAsync(database, repeated);
        await using var context = database.CreateContext();

        // Its key does not change, so nothing is checked before saving; the index refuses it.
        var result = await new UpdateCategoryCommandHandler(context)
            .Handle(new UpdateCategoryCommand { Id = repeated.Id, Name = "Bebidas", Description = "Frías" }, CancellationToken.None);

        Assert.Equal("Ya tienes una categoría archivada llamada «Bebidas». Restáurala o ponle otro nombre.", result.Error);
        await using var check = database.CreateIndependentContext();
        var stored = await check.Categories.SingleAsync(c => c.Id == repeated.Id);
        Assert.Equal(("Bebidas ", (string?)null), (stored.Name, stored.Description));
    }

    [Fact]
    public async Task InstallingATemplate_WithANameSavedMeanwhile_ShouldCreateNone_AndSayWhich()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var tenantId = Guid.NewGuid();
        database.SetTenant(tenantId);
        var template = CategoryTemplateCatalog.GetById("food")!;
        var racing = template.Categories.OrderBy(c => c.SortOrder).Last().Name;
        database.BeforeNextSave(() => SaveMeanwhileAsync(database, new Category(tenantId, racing, null)));
        await using var context = database.CreateContext();

        var result = await new InstallCategoryTemplateCommandHandler(context, database.TenantService)
            .Handle(new InstallCategoryTemplateCommand { TemplateId = "food" }, CancellationToken.None);

        Assert.Equal(InstallCategoryTemplateCommandHandler.TakenMeanwhile(racing), result.Error);
        await using var check = database.CreateIndependentContext();
        Assert.Equal([racing], await check.Categories.Select(c => c.Name).ToListAsync());
    }

    [Fact]
    public async Task InstallingATemplate_ShouldSkipTheNamesTheBusinessHas_WhateverTheirSpaces()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var template = CategoryTemplateCatalog.GetById("food")!;
        var first = template.Categories.OrderBy(c => c.SortOrder).First().Name;
        await AddCategoryAsync(fixture, tenant, $"  {first.ToUpperInvariant().Replace(" ", "\t")} ", archived: true);

        var result = await new InstallCategoryTemplateCommandHandler(fixture.Context, fixture.TenantService)
            .Handle(new InstallCategoryTemplateCommand { TemplateId = "food" }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal((template.Categories.Count - 1, 1), (result.Value.CreatedCount, result.Value.SkippedCount));
    }

    [Fact]
    public async Task StarterCatalog_ShouldReuseACategoryRepeatedBeforeTheRule_ActiveFirst()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food);
        var archived = await AddCategoryAsync(fixture, tenant, "café caliente", archived: true);
        var active = await AddCategoryAsync(fixture, tenant, " Café  caliente ");

        var result = await new InstallStarterCatalogCommandHandler(fixture.Context, fixture.TenantService)
            .Handle(new InstallStarterCatalogCommand { TemplateId = "cafeteria" }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var template = StarterCatalogTemplates.GetById("cafeteria")!;
        Assert.Equal(template.Categories.Count - 1, result.Value.CategoriesCreated);
        var hotCoffee = template.Categories.Single(c => c.Name == "Café caliente").Products.Count;
        Assert.Equal(hotCoffee, await fixture.Context.Products.CountAsync(p => p.CategoryId == active.Id));
        Assert.False(await fixture.Context.Products.AnyAsync(p => p.CategoryId == archived.Id));
    }

    private static async Task<Category> AddCategoryAsync(SalesTestFixture fixture, Tenant tenant, string name, bool archived = false)
    {
        var category = new Category(tenant.Id, name, null);
        if (archived)
            category.Deactivate();
        fixture.Context.Categories.Add(category);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return category;
    }

    private static async Task SaveMeanwhileAsync(SqliteTestDatabase database, Category category)
    {
        await using var other = database.CreateIndependentContext();
        other.Categories.Add(category);
        await other.SaveChangesAsync();
    }

    private static async Task<Result<CategoryDto>> CreateAsync(SalesTestFixture fixture, string name)
    {
        var result = await new CreateCategoryCommandHandler(fixture.Context, fixture.TenantService)
            .Handle(new CreateCategoryCommand { Name = name }, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static async Task<Result<CategoryDto>> UpdateAsync(
        SalesTestFixture fixture, Guid id, string name, string? description, int sortOrder = 0)
    {
        var result = await new UpdateCategoryCommandHandler(fixture.Context)
            .Handle(new UpdateCategoryCommand { Id = id, Name = name, Description = description, SortOrder = sortOrder }, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static Task<List<string>> NamesAsync(SalesTestFixture fixture)
        => fixture.Context.Categories.AsNoTracking().OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();
}
