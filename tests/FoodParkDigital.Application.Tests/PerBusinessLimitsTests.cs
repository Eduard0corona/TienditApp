using FoodParkDigital.Application.AiAgents.Commands;
using FoodParkDigital.Application.AiAgents.DTOs;
using FoodParkDigital.Application.Branches;
using FoodParkDigital.Application.Catalog.Commands;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Catalog.Templates;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Inventory.Commands;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.AiAgents.Entities;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The lists a business sees whole, without pages (categories, warehouses, agents, special days), stay small because a
/// business keeps only so many of each, the inactive ones included; another business's never count. The kitchen board
/// shows the newest orders, oldest first.
/// </summary>
public class PerBusinessLimitsTests
{
    private const TenantCapability Shop = TenantCapability.Retail | TenantCapability.Inventory;

    [Fact]
    public async Task Categories_ShouldStopAtTheLimit_CountingTheArchived_ButNotAnotherBusinesss()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedBusinessAsync(host, Shop, BusinessType.Retail, (db, id) => db.Categories.AddRange(
            Enumerable.Range(1, Category.MaxCategoriesPerTenant).Select(i => new Category(id, $"Ajena {i}", null))));
        await SeedBusinessAsync(host, Shop, BusinessType.Retail, (db, id) => db.Categories.AddRange(
            Enumerable.Range(1, Category.MaxCategoriesPerTenant - 1).Select(i => Archived(new Category(id, $"Propia {i}", null), i))));

        var last = await CreateCategoryAsync(host, "La última");
        var oneMore = await CreateCategoryAsync(host, "Una más");

        Assert.True(last.IsSuccess, last.Error);
        Assert.False(oneMore.IsSuccess);
        Assert.Equal(CategoryLimits.Reached, oneMore.Error);
        Assert.Equal(Category.MaxCategoriesPerTenant, await host.QueryAsync(db => db.Categories.CountAsync()));
    }

    [Fact]
    public async Task CategoryTemplate_ShouldInstallWhole_OrNotAtAll_WithinTheLimit()
    {
        var template = CategoryTemplateCatalog.GetById("food")!;
        var fits = Category.MaxCategoriesPerTenant - template.Categories.Count;

        await using var full = await SqliteTestHost.CreateAsync();
        await SeedBusinessAsync(full, Shop, BusinessType.Food, (db, id) => db.Categories.AddRange(
            Enumerable.Range(1, fits + 1).Select(i => new Category(id, $"Propia {i}", null))));
        var refused = await InstallAsync(full, template.Id);

        await using var room = await SqliteTestHost.CreateAsync();
        await SeedBusinessAsync(room, Shop, BusinessType.Food, (db, id) => db.Categories.AddRange(
            Enumerable.Range(1, fits).Select(i => new Category(id, $"Propia {i}", null))));
        var installed = await InstallAsync(room, template.Id);

        Assert.False(refused.IsSuccess);
        Assert.Equal(CategoryLimits.Reached, refused.Error);
        Assert.Equal(fits + 1, await full.QueryAsync(db => db.Categories.CountAsync()));
        Assert.True(installed.IsSuccess, installed.Error);
        Assert.Equal(template.Categories.Count, installed.Value.CreatedCount);
        Assert.Equal(Category.MaxCategoriesPerTenant, await room.QueryAsync(db => db.Categories.CountAsync()));
    }

    [Fact]
    public async Task Locations_ShouldStopAtTheLimit_ForANewWarehouseOrTheOneOfANewBranch()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedBusinessAsync(host, Shop, BusinessType.Retail, (db, id) => db.InventoryLocations.AddRange(Locations(id, "AJ")));
        await SeedBusinessAsync(host, Shop, BusinessType.Retail, (db, id) => db.InventoryLocations.AddRange(Locations(id, "PR")));

        var warehouse = await host.HandleAsync<CreateInventoryLocationCommandHandler, Result<InventoryLocationDto>>(handler => handler.Handle(
            new CreateInventoryLocationCommand { Name = "Bodega extra", Code = "EXTRA" }, CancellationToken.None));
        var branch = await host.HandleAsync<CreateBranchCommandHandler, Result<BranchDto>>(handler => handler.Handle(
            new CreateBranchCommand { Name = "Norte", Code = "NTE" }, CancellationToken.None));

        Assert.Equal(InventoryLocationLimits.Reached, warehouse.Error);
        Assert.Equal(InventoryLocationLimits.Reached, branch.Error);
        Assert.Equal(InventoryLocation.MaxLocationsPerTenant, await host.QueryAsync(db => db.InventoryLocations.CountAsync()));
        Assert.False(await host.QueryAsync(db => db.Branches.AnyAsync(b => b.Code == "NTE")));
    }

    [Fact]
    public async Task Locations_ShouldNotCountAnotherBusinesss()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedBusinessAsync(host, Shop, BusinessType.Retail, (db, id) => db.InventoryLocations.AddRange(Locations(id, "AJ")));
        await SeedBusinessAsync(host, Shop, BusinessType.Retail, (db, id) => db.InventoryLocations.AddRange(Locations(id, "PR").Skip(2)));

        var warehouse = await host.HandleAsync<CreateInventoryLocationCommandHandler, Result<InventoryLocationDto>>(handler => handler.Handle(
            new CreateInventoryLocationCommand { Name = "Bodega extra", Code = "EXTRA" }, CancellationToken.None));
        var branch = await host.HandleAsync<CreateBranchCommandHandler, Result<BranchDto>>(handler => handler.Handle(
            new CreateBranchCommand { Name = "Norte", Code = "NTE" }, CancellationToken.None));

        Assert.True(warehouse.IsSuccess, warehouse.Error);
        Assert.True(branch.IsSuccess, branch.Error);
        Assert.Equal(InventoryLocation.MaxLocationsPerTenant, await host.QueryAsync(db => db.InventoryLocations.CountAsync()));
    }

    [Fact]
    public async Task Agents_ShouldStopAtTheLimit_CountingTheInactive_ButNotAnotherBusinesss()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedBusinessAsync(host, Shop, BusinessType.Retail, (db, id) => db.AiAgents.AddRange(Agents(id, AiAgent.MaxAgentsPerTenant)));
        await SeedBusinessAsync(host, Shop, BusinessType.Retail, (db, id) => db.AiAgents.AddRange(Agents(id, AiAgent.MaxAgentsPerTenant - 1)));

        var last = await CreateAgentAsync(host, "El último");
        var oneMore = await CreateAgentAsync(host, "Uno más");

        Assert.True(last.IsSuccess, last.Error);
        Assert.False(oneMore.IsSuccess);
        Assert.Contains($"{AiAgent.MaxAgentsPerTenant} agentes", oneMore.Error);
        Assert.Equal(AiAgent.MaxAgentsPerTenant, await host.QueryAsync(db => db.AiAgents.CountAsync()));
    }

    [Fact]
    public async Task SpecialDates_ShouldStopAtTheLimit_CountingOnlyTheUpcoming_ButNotAnotherBusinesss()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedBusinessAsync(host, Shop, BusinessType.Retail, (db, id) => db.SpecialDays.AddRange(UpcomingSpecialDates(id, SpecialDay.MaxUpcomingDates)));
        await SeedBusinessAsync(host, Shop, BusinessType.Retail, (db, id) => db.SpecialDays.AddRange(
            UpcomingSpecialDates(id, SpecialDay.MaxUpcomingDates - 1).Concat(PastSpecialDates(id))));

        var last = await AddSpecialDayAsync(host, new DateOnly(2026, 12, 1));
        var oneMore = await Assert.ThrowsAsync<DomainException>(() => AddSpecialDayAsync(host, new DateOnly(2026, 12, 2)));

        Assert.True(last.IsSuccess, last.Error);
        Assert.Equal(SpecialDay.MaxUpcomingDates, last.Value.UpcomingDates);
        Assert.Equal("Puedes tener hasta 120 fechas especiales próximas. Quita alguna antes de agregar otra.", oneMore.Message);
        Assert.Equal(SpecialDay.MaxUpcomingDates + 10, await host.QueryAsync(db => db.SpecialDays.CountAsync()));
    }

    [Fact]
    public async Task KitchenBoard_ShouldShowTheNewestOrders_OldestFirst()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var waiting = GetKitchenOrdersQueryHandler.MaxOrders + 1;
        await SeedBusinessAsync(host, TenantCapability.Food | TenantCapability.Kitchen, BusinessType.Food, (db, id) => db.Orders.AddRange(
            Enumerable.Range(1, waiting).Select(i => new Order(
                id, null, null, createdByBusiness: true, $"K-{i:0000}", BusinessType.Food, OrderType.Retail, "Mostrador", null, null, null))));

        var board = await host.HandleAsync<GetKitchenOrdersQueryHandler, List<KitchenOrderDto>>(handler => handler.Handle(
            new GetKitchenOrdersQuery(), CancellationToken.None));

        // All of them were taken at the same time: the newest folios win, and the board lists them in order.
        Assert.Equal(GetKitchenOrdersQueryHandler.MaxOrders, board.Count);
        Assert.Equal("K-0002", board[0].OrderNumber);
        Assert.Equal($"K-{waiting:0000}", board[^1].OrderNumber);
        Assert.Equal(board.Select(o => o.OrderNumber).Order(StringComparer.Ordinal), board.Select(o => o.OrderNumber));
    }

    /// <summary>A business with its rows, saved with it; the last one seeded is the one in context.</summary>
    private static async Task SeedBusinessAsync(
        SqliteTestHost host, TenantCapability capabilities, BusinessType businessType, Action<ApplicationDbContext, Guid> seed)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Negocio {slug}", slug, businessType, $"{slug}@demo.com");
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.Tenants.Add(tenant);
            db.TenantSettings.Add(new TenantSettings(tenant.Id, capabilities, pricesIncludeTax: true));
            db.Branches.Add(Branch.CreateDefault(tenant.Id));
            seed(db, tenant.Id);
            await db.SaveChangesAsync();
        });
        host.SetTenant(tenant.Id);
    }

    private static Category Archived(Category category, int index)
    {
        if (index % 2 == 0)
            category.Deactivate();
        return category;
    }

    private static IEnumerable<InventoryLocation> Locations(Guid tenantId, string prefix)
        => Enumerable.Range(1, InventoryLocation.MaxLocationsPerTenant).Select(i =>
        {
            var location = new InventoryLocation(tenantId, $"Bodega {prefix} {i}", $"{prefix}{i:000}", isDefault: i == 1);
            if (i % 3 == 0)
                location.SetActive(false);
            return location;
        });

    private static IEnumerable<AiAgent> Agents(Guid tenantId, int count)
        => Enumerable.Range(1, count).Select(i =>
        {
            var agent = new AiAgent(tenantId, $"Agente {i}", "Ventas");
            if (i % 2 == 0)
                agent.SetActive(false);
            return agent;
        });

    /// <summary>Closed dates from March on, for SqliteTestHost's today (2026-02-16), in periods as long as they can be.</summary>
    private static List<SpecialDay> UpcomingSpecialDates(Guid tenantId, int count)
    {
        var calendar = new SpecialDayCalendar(tenantId, new DateOnly(2026, 2, 16), []);
        var rows = new List<SpecialDay>();
        for (var from = new DateOnly(2026, 3, 1); rows.Count < count; from = from.AddDays(SpecialDay.MaxDaysPerPeriod))
        {
            var days = Math.Min(count - rows.Count, SpecialDay.MaxDaysPerPeriod);
            rows.AddRange(calendar.Add(from, from.AddDays(days - 1), SpecialDayRule.Closed(null)).Added);
        }
        return rows;
    }

    /// <summary>Ten closed dates marked in January, which already passed.</summary>
    private static IEnumerable<SpecialDay> PastSpecialDates(Guid tenantId)
        => new SpecialDayCalendar(tenantId, new DateOnly(2026, 1, 1), [])
            .Add(new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 19), SpecialDayRule.Closed("Inventario"))
            .Added;

    private static Task<Result<SpecialDaysDto>> AddSpecialDayAsync(SqliteTestHost host, DateOnly date)
        => host.HandleAsync<AddSpecialDayCommandHandler, Result<SpecialDaysDto>>(handler => handler.Handle(
            new AddSpecialDayCommand(date, null, SpecialDayKind.Closed, null, null), CancellationToken.None));

    private static Task<Result<CategoryDto>> CreateCategoryAsync(SqliteTestHost host, string name)
        => host.HandleAsync<CreateCategoryCommandHandler, Result<CategoryDto>>(handler => handler.Handle(
            new CreateCategoryCommand { Name = name }, CancellationToken.None));

    private static Task<Result<InstallCategoryTemplateResultDto>> InstallAsync(SqliteTestHost host, string templateId)
        => host.HandleAsync<InstallCategoryTemplateCommandHandler, Result<InstallCategoryTemplateResultDto>>(handler => handler.Handle(
            new InstallCategoryTemplateCommand { TemplateId = templateId }, CancellationToken.None));

    private static Task<Result<AiAgentDto>> CreateAgentAsync(SqliteTestHost host, string name)
        => host.HandleAsync<CreateAiAgentCommandHandler, Result<AiAgentDto>>(handler => handler.Handle(
            new CreateAiAgentCommand { Name = name, Role = "Ventas" }, CancellationToken.None));
}
