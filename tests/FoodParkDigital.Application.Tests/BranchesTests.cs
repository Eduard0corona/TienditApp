using FoodParkDigital.Application.Branches;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Finance.Commands;
using FoodParkDigital.Application.Finance.Queries;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Common.ValueObjects;
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
    public async Task Branch_ShouldBeAddedAsDeliveryOnly_AndKeepIt_WhenASaveDoesNotSayHowItServes()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedTenantAsync(host, TenantCapability.Food);

        var kitchen = await CreateAsync(host, new CreateBranchCommand
        {
            Name = "Cocina", Code = "COC", AddressLine1 = "Privada Los Pinos 8", ServiceMode = BranchServiceMode.DeliveryOnly
        });
        var centro = await CreateAsync(host, new CreateBranchCommand { Name = "Centro", Code = "CEN" });
        Assert.True(kitchen.IsSuccess, kitchen.Error);
        Assert.Equal(BranchServiceMode.DeliveryOnly, kitchen.Value.ServiceMode);
        // Without saying how it serves, a new branch has premises, as before.
        Assert.Equal(BranchServiceMode.Premises, centro.Value.ServiceMode);

        // A screen that does not know the field (an old tab) never turns the kitchen into premises with a public address.
        var renamed = await UpdateAsync(host, new UpdateBranchCommand
        {
            Id = kitchen.Value.Id, Name = "Cocina norte", Code = "COC", AddressLine1 = "Privada Los Pinos 8"
        });
        Assert.True(renamed.IsSuccess, renamed.Error);
        Assert.Equal(("Cocina norte", BranchServiceMode.DeliveryOnly), (renamed.Value.Name, renamed.Value.ServiceMode));

        var opened = await UpdateAsync(host, new UpdateBranchCommand
        {
            Id = kitchen.Value.Id, Name = "Cocina norte", Code = "COC", ServiceMode = BranchServiceMode.Premises
        });
        Assert.Equal(BranchServiceMode.Premises, opened.Value.ServiceMode);
        Assert.Equal(BranchServiceMode.Premises, (await host.QueryAsync(db => db.Branches.SingleAsync(b => b.Code == "COC"))).ServiceMode);
    }

    [Fact]
    public void BranchCommands_ShouldRefuseAServiceModeTheyDoNotKnow()
    {
        var create = new CreateBranchCommandValidator().Validate(
            new CreateBranchCommand { Name = "Cocina", Code = "COC", ServiceMode = (BranchServiceMode)7 });
        var update = new UpdateBranchCommandValidator().Validate(
            new UpdateBranchCommand { Id = Guid.NewGuid(), Name = "Cocina", Code = "COC", ServiceMode = (BranchServiceMode)7 });

        Assert.Equal(Branch.ServiceModeMessage, Assert.Single(create.Errors).ErrorMessage);
        Assert.Equal(Branch.ServiceModeMessage, Assert.Single(update.Errors).ErrorMessage);
    }

    [Fact]
    public async Task Branch_ShouldBePutOnTheMap_KeepItsPoint_WhenASaveDoesNotSayWhere_AndBeTakenOffWithNeitherHalf()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedTenantAsync(host, TenantCapability.Food);

        var centro = await CreateAsync(host, new CreateBranchCommand
        {
            Name = "Centro", Code = "CEN", Location = new BranchLocationFields { Latitude = 19.0435774m, Longitude = -98.1986262m }
        });
        var norte = await CreateAsync(host, new CreateBranchCommand { Name = "Norte", Code = "NTE" });
        Assert.True(centro.IsSuccess, centro.Error);
        // Six decimals, as Google Maps copies them; a branch saved without a point is not on the map.
        Assert.Equal((19.043577m, -98.198626m), (centro.Value.Latitude, centro.Value.Longitude));
        Assert.Equal((null, null), (norte.Value.Latitude, norte.Value.Longitude));

        // A screen that does not know the field (an old tab) never takes the branch off the map.
        var renamed = await UpdateAsync(host, new UpdateBranchCommand { Id = centro.Value.Id, Name = "Centro histórico", Code = "CEN" });
        Assert.Equal(("Centro histórico", 19.043577m), (renamed.Value.Name, renamed.Value.Latitude));

        var moved = await UpdateAsync(host, new UpdateBranchCommand
        {
            Id = centro.Value.Id, Name = "Centro histórico", Code = "CEN", Location = new BranchLocationFields { Latitude = 19.05m, Longitude = -98.2m }
        });
        Assert.Equal((19.05m, -98.2m), (moved.Value.Latitude, moved.Value.Longitude));

        var off = await UpdateAsync(host, new UpdateBranchCommand
        {
            Id = centro.Value.Id, Name = "Centro histórico", Code = "CEN", Location = new BranchLocationFields()
        });
        Assert.True(off.IsSuccess, off.Error);
        Assert.Null((await host.QueryAsync(db => db.Branches.SingleAsync(b => b.Code == "CEN"))).Location);
    }

    [Theory]
    [InlineData(19.04, null, BranchFieldRules.HalfAPointMessage)]
    [InlineData(null, -98.19, BranchFieldRules.HalfAPointMessage)]
    [InlineData(91.0, -98.19, GeoPoint.InvalidMessage)]
    [InlineData(19.04, -180.5, GeoPoint.InvalidMessage)]
    [InlineData(0.0, 0.0, GeoPoint.NullIslandMessage)]
    public void BranchCommands_ShouldRefuse_HalfAPoint_APointOffTheMap_OrZeroZero(double? latitude, double? longitude, string message)
    {
        var location = new BranchLocationFields { Latitude = (decimal?)latitude, Longitude = (decimal?)longitude };

        var create = new CreateBranchCommandValidator().Validate(new CreateBranchCommand { Name = "Centro", Code = "CEN", Location = location });
        var update = new UpdateBranchCommandValidator().Validate(
            new UpdateBranchCommand { Id = Guid.NewGuid(), Name = "Centro", Code = "CEN", Location = location });

        Assert.Equal(message, Assert.Single(create.Errors).ErrorMessage);
        Assert.Equal(message, Assert.Single(update.Errors).ErrorMessage);
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
        // Other cashiers' drawers take the payments; only the seller's own drawer decides the branch of a sale.
        await fixture.OpenCashSessionAsync(tenant, main.Id, Guid.NewGuid());
        await fixture.OpenCashSessionAsync(tenant, norte.Id, Guid.NewGuid());

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
        await fixture.OpenCashSessionAsync(tenant, norte.Id);
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
        await fixture.OpenCashSessionAsync(tenant, (await fixture.DefaultBranchAsync()).Id);
        await fixture.OpenCashSessionAsync(tenant, norte.Id);
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

    private static Task<Result<BranchDto>> UpdateAsync(SqliteTestHost host, UpdateBranchCommand command)
        => host.HandleAsync<UpdateBranchCommandHandler, Result<BranchDto>>(handler => handler.Handle(command, CancellationToken.None));

    private static Task<Result> SetActiveAsync(SqliteTestHost host, Guid id, bool isActive)
        => host.HandleAsync<SetBranchActiveCommandHandler, Result>(
            handler => handler.Handle(new SetBranchActiveCommand { Id = id, IsActive = isActive }, CancellationToken.None));
}
