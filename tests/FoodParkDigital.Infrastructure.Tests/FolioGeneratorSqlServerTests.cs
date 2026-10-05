using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Inventory.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// Each business has its own folio counters: two cash registers saving at the same time never get the same folio,
/// and a counter continues after the folios the business already had.
/// </summary>
public class FolioGeneratorSqlServerTests
{
    private static readonly DateTimeOffset Today = new(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task ConcurrentSales_ShouldEachGetTheirOwnFolio()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenantId = await SeedTenantAsync(database.CreateContext());

        var folios = await Task.WhenAll(Enumerable.Range(0, 25).Select(async _ =>
        {
            await using var context = database.CreateContext();
            return await new FolioGenerator(context).NextOrderNumberAsync(tenantId, CancellationToken.None);
        }));

        Assert.Equal(Enumerable.Range(1, 25).Select(n => Folios.OrderNumber(n)), folios.Order());
    }

    [SqlServerFact]
    public async Task BusinessWithoutCounter_ShouldContinueAfterItsFolios()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var tenantId = await SeedTenantAsync(context, "ORD-000007", "ORD-000003");
        var otherTenant = await SeedTenantAsync(context, "ORD-000040");

        var next = await new FolioGenerator(context).NextOrderNumberAsync(tenantId, CancellationToken.None);
        var following = await new FolioGenerator(context).NextOrderNumberAsync(tenantId, CancellationToken.None);
        var other = await new FolioGenerator(context).NextOrderNumberAsync(otherTenant, CancellationToken.None);

        Assert.Equal("ORD-000008", next);
        Assert.Equal("ORD-000009", following);
        Assert.Equal("ORD-000041", other);
    }

    [SqlServerFact]
    public async Task ConcurrentMovements_ShouldEachGetTheirOwnFolio_AfterTheOnesOfTheDay()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        Guid tenantId;
        await using (var context = database.CreateContext())
        {
            tenantId = await SeedTenantAsync(context);
            // Movements numbered before the counters existed: today's continue after them, yesterday's do not count.
            context.InventoryMovements.AddRange(
                Movement(tenantId, "INV-20261005-0002"),
                Movement(tenantId, "INV-20261004-0031"));
            await context.SaveChangesAsync();
        }

        var folios = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var context = database.CreateContext();
            return await new FolioGenerator(context).NextInventoryMovementNumberAsync(tenantId, Today, CancellationToken.None);
        }));
        await using var check = database.CreateContext();
        var tomorrow = await new FolioGenerator(check).NextInventoryMovementNumberAsync(tenantId, Today.AddDays(1), CancellationToken.None);
        var purchase = await new FolioGenerator(check).NextPurchaseOrderNumberAsync(tenantId, Today, CancellationToken.None);

        Assert.Equal(Enumerable.Range(3, 20).Select(n => $"INV-20261005-{n:0000}"), folios.Order());
        Assert.Equal("INV-20261006-0001", tomorrow);
        Assert.Equal("PO-20261005-0001", purchase);
    }

    private static InventoryMovement Movement(Guid tenantId, string number)
        => new(tenantId, number, InventoryMovementType.Entry, InventoryMovementReason.Purchase, Today);

    private static async Task<Guid> SeedTenantAsync(ApplicationDbContext context, params string[] folios)
    {
        var tenant = new Tenant("Café", Guid.NewGuid().ToString("N")[..10], BusinessType.Retail, $"{Guid.NewGuid():N}@demo.com");
        context.Tenants.Add(tenant);
        foreach (var folio in folios)
        {
            context.Orders.Add(new Order(
                tenant.Id, null, null, true, folio, BusinessType.Retail, OrderType.Retail, "Público en general", null, null, null));
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return tenant.Id;
    }
}
