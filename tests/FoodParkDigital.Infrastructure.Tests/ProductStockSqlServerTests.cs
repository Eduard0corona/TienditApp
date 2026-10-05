using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The stock of products that control it is read from the inventory ledger in SQL Server: their item's stock in every
/// location.
/// </summary>
public class ProductStockSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task ProductsThatControlTheirStock_ShouldShowWhatTheirItemHasInEveryLocation()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenantId, soda, chips, water) = await SeedAsync(database);

        await using var context = database.CreateContext(tenantId);
        var products = await context.Products.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(CatalogMappings.AsDto)
            .ToListAsync();
        await ProductStockReader.ApplyAsync(context, products, CancellationToken.None);
        var sodaStock = await ProductStockReader.GetAsync(context, soda, CancellationToken.None);

        var bySoda = products.Single(p => p.Id == soda);
        Assert.True(bySoda.TracksStock);
        Assert.Equal(13, bySoda.OnHand);
        Assert.False(bySoda.IsLowStock);
        var byChips = products.Single(p => p.Id == chips);
        Assert.True(byChips.TracksStock);
        Assert.Equal(0, byChips.OnHand);
        Assert.True(byChips.IsLowStock);
        var byWater = products.Single(p => p.Id == water);
        Assert.False(byWater.TracksStock);
        Assert.Null(byWater.OnHand);
        Assert.Equal(new[] { ("Almacén", 5m), ("Mostrador", 8m) }, sodaStock!.Locations.Select(l => (l.LocationName, l.OnHand)));
        Assert.Equal(13, sodaStock.OnHand);
    }

    private static async Task<(Guid TenantId, Guid Soda, Guid Chips, Guid Water)> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant("Tienda", slug, BusinessType.Retail, $"{slug}@demo.com");
        var category = new Category(tenant.Id, "General", null);
        var soda = new Product(tenant.Id, category.Id, "Refresco", null, 20m, "MXN");
        var chips = new Product(tenant.Id, category.Id, "Papas", null, 18m, "MXN");
        var water = new Product(tenant.Id, category.Id, "Agua", null, 12m, "MXN");
        var counter = new InventoryLocation(tenant.Id, "Mostrador", $"MOS-{slug}", isDefault: true);
        var storage = new InventoryLocation(tenant.Id, "Almacén", $"ALM-{slug}");
        var sodaItem = SupplyItem.ForProduct(tenant.Id, soda.Id, "Refresco", UnitType.Unit, null, 12m, reorderPoint: 4);
        var chipsItem = SupplyItem.ForProduct(tenant.Id, chips.Id, "Papas", UnitType.Unit, null, 9m, reorderPoint: 2);

        await using var context = database.CreateContext(tenant.Id);
        context.AddRange(tenant, category, soda, chips, water, counter, storage, sodaItem, chipsItem);
        context.InventoryStocks.AddRange(Stock(tenant.Id, sodaItem, counter, 8), Stock(tenant.Id, sodaItem, storage, 5));
        await context.SaveChangesAsync();
        return (tenant.Id, soda.Id, chips.Id, water.Id);
    }

    private static InventoryStock Stock(Guid tenantId, SupplyItem item, InventoryLocation location, decimal quantity)
    {
        var stock = new InventoryStock(tenantId, item.Id, location.Id);
        stock.ApplyMovement(quantity, null, Now);
        return stock;
    }
}
