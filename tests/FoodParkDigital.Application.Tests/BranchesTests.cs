using FoodParkDigital.Application.Branches;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Finance.Commands;
using FoodParkDigital.Application.Finance.Queries;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>Branches as their own entity: managing them, and the sales, drawers and stock that belong to each one.</summary>
public class BranchesTests
{
    [Fact]
    public async Task NewBranch_ShouldGetItsStockLocation_AndTheDefaultMarkShouldMoveToIt()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedTenantAsync(host, TenantCapability.Retail | TenantCapability.Inventory);

        var norte = await CreateAsync(host, new CreateBranchCommand { Name = "Norte", Code = "nte", City = "Monterrey" });
        Assert.True(norte.IsSuccess, norte.Error);
        Assert.False(norte.Value.IsDefault);
        Assert.Equal("NTE", norte.Value.Code);

        var moved = await host.HandleAsync<SetDefaultBranchCommandHandler, Result>(
            handler => handler.Handle(new SetDefaultBranchCommand { Id = norte.Value.Id }, CancellationToken.None));
        Assert.True(moved.IsSuccess, moved.Error);

        var branches = await host.QueryAsync(db => db.Branches.ToListAsync());
        var location = await host.QueryAsync(db => db.InventoryLocations.SingleAsync(l => l.BranchId == norte.Value.Id));
        Assert.Equal("Norte", Assert.Single(branches, b => b.IsDefault).Name);
        Assert.Equal("NTE", location.Code);
        Assert.Equal("Monterrey", location.City);
    }

    [Fact]
    public async Task NewBranch_ShouldNotGetAStockLocation_WhenTheBusinessHasNoInventory()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedTenantAsync(host, TenantCapability.Service);

        var norte = await CreateAsync(host, new CreateBranchCommand { Name = "Norte", Code = "NTE", IsDefault = true });

        Assert.True(norte.IsSuccess, norte.Error);
        Assert.True(norte.Value.IsDefault);
        Assert.Empty(await host.QueryAsync(db => db.InventoryLocations.ToListAsync()));
        Assert.Single(await host.QueryAsync(db => db.Branches.Where(b => b.IsDefault).ToListAsync()));
    }

    [Fact]
    public async Task BranchCode_ShouldBeUniqueInTheBusiness_WhateverItsCase()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedTenantAsync(host, TenantCapability.Retail);

        var first = await CreateAsync(host, new CreateBranchCommand { Name = "Norte", Code = "NTE" });
        var again = await CreateAsync(host, new CreateBranchCommand { Name = "Norte 2", Code = " nte " });

        Assert.True(first.IsSuccess, first.Error);
        Assert.False(again.IsSuccess);
        Assert.Contains("clave", again.Error);
    }

    [Fact]
    public async Task Deactivating_ShouldBeRefused_ForTheDefaultBranchOrOneWithOpenDrawers()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var tenantId = await SeedTenantAsync(host, TenantCapability.Retail);
        var norte = (await CreateAsync(host, new CreateBranchCommand { Name = "Norte", Code = "NTE" })).Value;
        var main = await host.QueryAsync(db => db.Branches.SingleAsync(b => b.IsDefault));
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.CashSessions.Add(new CashSession(tenantId, DateTimeOffset.UtcNow, "ana", 100m, branchId: norte.Id));
            await db.SaveChangesAsync();
        });

        var defaultOff = await SetActiveAsync(host, main.Id, false);
        var withDrawer = await SetActiveAsync(host, norte.Id, false);

        Assert.False(defaultOff.IsSuccess);
        Assert.Contains("principal", defaultOff.Error);
        Assert.False(withDrawer.IsSuccess);
        Assert.Contains("cajas abiertas", withDrawer.Error);
    }

    [Fact]
    public async Task Sale_ShouldBeMadeAtTheBranchAskedFor_ElseAtTheCashiersDrawer_ElseAtTheDefault()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        var main = await fixture.DefaultBranchAsync();
        var norte = await fixture.AddBranchAsync(tenant, "Norte", "NTE");

        var asked = await SellAsync(fixture, soda.Id, branchId: norte.Id);
        var atDefault = await SellAsync(fixture, soda.Id);
        await fixture.OpenCashSessionAsync(tenant, norte.Id, Guid.Parse(fixture.CurrentUser.UserId!));
        var atDrawer = await SellAsync(fixture, soda.Id);

        Assert.Equal(norte.Id, await BranchOfAsync(fixture, asked));
        Assert.Equal(main.Id, await BranchOfAsync(fixture, atDefault));
        Assert.Equal(norte.Id, await BranchOfAsync(fixture, atDrawer));
    }

    [Fact]
    public async Task Sale_ShouldBeRejected_AtAnInactiveBranchOrOneOfAnotherBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "otro");
        var foreignBranch = await fixture.DefaultBranchAsync();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        var closed = await fixture.AddBranchAsync(tenant, "Cerrada", "CER");
        closed.Deactivate();
        await fixture.Context.SaveChangesAsync();

        var atClosed = await fixture.CreateOrderHandler().Handle(Sale(soda.Id, closed.Id), CancellationToken.None);
        var atForeign = await fixture.CreateOrderHandler().Handle(Sale(soda.Id, foreignBranch.Id), CancellationToken.None);

        Assert.NotEqual(other.Id, tenant.Id);
        Assert.Equal(BranchRules.InvalidBranchMessage, atClosed.Error);
        Assert.Equal(BranchRules.InvalidBranchMessage, atForeign.Error);
    }

    [Fact]
    public async Task CashOfASale_ShouldEnterTheDrawerOfItsBranch()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        var main = await fixture.DefaultBranchAsync();
        var norte = await fixture.AddBranchAsync(tenant, "Norte", "NTE");
        var mainDrawer = await fixture.OpenCashSessionAsync(tenant, main.Id, Guid.NewGuid());
        var norteDrawer = await fixture.OpenCashSessionAsync(tenant, norte.Id, Guid.NewGuid());

        var sale = await fixture.CreateOrderHandler().Handle(Sale(soda.Id, norte.Id, paid: 23.20m), CancellationToken.None);

        Assert.True(sale.IsSuccess, sale.Error);
        Assert.Empty(await fixture.Context.CashTransactions.Where(t => t.CashSessionId == mainDrawer.Id).ToListAsync());
        Assert.Equal(23.20m, (await fixture.Context.CashTransactions.SingleAsync(t => t.CashSessionId == norteDrawer.Id)).Amount);
    }

    [Fact]
    public async Task Sale_ShouldTakeStockFromItsBranch()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food | TenantCapability.Inventory, pricesIncludeTax: true);
        var taco = await fixture.AddProductAsync(tenant, "Taco", 25m);
        var norte = await fixture.AddBranchAsync(tenant, "Norte", "NTE");
        var tortilla = new SupplyItem(tenant.Id, "Tortilla", UnitType.Unit);
        fixture.Context.SupplyItems.Add(tortilla);
        foreach (var location in await fixture.Context.InventoryLocations.ToListAsync())
        {
            var stock = new InventoryStock(tenant.Id, tortilla.Id, location.Id);
            stock.ApplyMovement(100m, 1m, SalesTestFixture.Now);
            fixture.Context.InventoryStocks.Add(stock);
        }
        fixture.Context.ProductSupplyRequirements.Add(new ProductSupplyRequirement(tenant.Id, taco.Id, tortilla.Id, 2m));
        await fixture.Context.SaveChangesAsync();
        var inventory = new OrderInventoryService(fixture.Context, fixture.Clock, new FolioGenerator(fixture.Context), NullLogger<OrderInventoryService>.Instance);

        var sale = await fixture.CreateOrderHandler(inventory).Handle(Sale(taco.Id, norte.Id, quantity: 3, paid: 75m), CancellationToken.None);

        Assert.True(sale.IsSuccess, sale.Error);
        var stocks = await fixture.Context.InventoryStocks.AsNoTracking()
            .Join(fixture.Context.InventoryLocations, s => s.InventoryLocationId, l => l.Id, (s, l) => new { l.BranchId, s.OnHandQuantity })
            .ToListAsync();
        Assert.Equal(94m, stocks.Single(s => s.BranchId == norte.Id).OnHandQuantity);
        Assert.Equal(100m, stocks.Single(s => s.BranchId != norte.Id).OnHandQuantity);
    }

    [Fact]
    public async Task Drawer_ShouldOpenAtTheBranchAskedFor_WithTheBranchsStockLocation()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var norte = await fixture.AddBranchAsync(tenant, "Norte", "NTE");

        var opened = await new OpenCashSessionCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock)
            .Handle(new OpenCashSessionCommand { OpeningAmount = 300m, BranchId = norte.Id }, CancellationToken.None);

        Assert.True(opened.IsSuccess, opened.Error);
        Assert.Equal(norte.Id, opened.Value.BranchId);
        Assert.Equal("Norte", opened.Value.BranchName);
        Assert.Equal("Almacén Norte", opened.Value.InventoryLocationName);
    }

    [Fact]
    public async Task DailyCut_ShouldFilterAndGroupPaymentsByTheBranchOfTheSale()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail);
        var soda = await fixture.AddProductAsync(tenant, "Refresco", 20m);
        var norte = await fixture.AddBranchAsync(tenant, "Norte", "NTE");
        await SellAsync(fixture, soda.Id, branchId: norte.Id);
        await SellAsync(fixture, soda.Id);
        var handler = new GetDailyCashCutQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock);

        var all = await handler.Handle(new GetDailyCashCutQuery(), CancellationToken.None);
        var onlyNorte = await handler.Handle(new GetDailyCashCutQuery { BranchId = norte.Id }, CancellationToken.None);

        Assert.Equal(["Matriz", "Norte"], all.ByBranch.Select(b => b.BranchName).Order());
        Assert.Equal(46.40m, all.SalesPaymentsTotal);
        Assert.Equal(23.20m, onlyNorte.SalesPaymentsTotal);
        Assert.Equal("Norte", Assert.Single(onlyNorte.ByBranch).BranchName);
    }

    private static CreateOrderCommand Sale(Guid productId, Guid? branchId = null, int quantity = 1, decimal paid = 23.20m) => new()
    {
        BranchId = branchId,
        Items = [new CreateOrderItemDto { ProductId = productId, Quantity = quantity }],
        Payments = [new CreateOrderPaymentDto { Amount = paid, Method = OrderPaymentMethod.Cash }]
    };

    private static async Task<Guid> SellAsync(SalesTestFixture fixture, Guid productId, Guid? branchId = null)
    {
        var sale = await fixture.CreateOrderHandler().Handle(Sale(productId, branchId), CancellationToken.None);
        Assert.True(sale.IsSuccess, sale.Error);
        return sale.Value.Id;
    }

    private static async Task<Guid?> BranchOfAsync(SalesTestFixture fixture, Guid orderId)
        => (await fixture.Context.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId)).BranchId;

    private static async Task<Guid> SeedTenantAsync(SqliteTestHost host, TenantCapability capabilities)
    {
        var tenant = new Tenant("Café Luna", "cafe-luna", BusinessType.Retail, "luna@demo.com");
        host.SetTenant(tenant.Id);
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.Tenants.Add(tenant);
            db.TenantSettings.Add(new TenantSettings(tenant.Id, capabilities, pricesIncludeTax: true));
            db.Branches.Add(Branch.CreateDefault(tenant.Id));
            await db.SaveChangesAsync();
        });
        return tenant.Id;
    }

    private static Task<Result<BranchDto>> CreateAsync(SqliteTestHost host, CreateBranchCommand command)
        => host.HandleAsync<CreateBranchCommandHandler, Result<BranchDto>>(handler => handler.Handle(command, CancellationToken.None));

    private static Task<Result> SetActiveAsync(SqliteTestHost host, Guid id, bool isActive)
        => host.HandleAsync<SetBranchActiveCommandHandler, Result>(
            handler => handler.Handle(new SetBranchActiveCommand { Id = id, IsActive = isActive }, CancellationToken.None));
}
