using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.BackgroundJobs;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// Order lines and status changes belong to the business of their order and are filtered by it. The ones the previous
/// version adds while the migration is applied (with no business) are completed when the new version starts.
/// </summary>
public class OrderTenantBackfillSqlServerTests
{
    private static readonly Guid NoBusiness = Guid.Empty;

    [SqlServerFact]
    public async Task LinesWithoutABusiness_ShouldBeHidden_UntilTheyGetTheBusinessOfTheirOrder()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenant = new Tenant("Fonda", "fonda-" + Guid.NewGuid().ToString("N")[..8], BusinessType.Food, $"{Guid.NewGuid():N}@demo.com");
        var other = new Tenant("Otra", "otra-" + Guid.NewGuid().ToString("N")[..8], BusinessType.Food, $"{Guid.NewGuid():N}@demo.com");
        var order = new Order(tenant.Id, null, null, true, "ORD-000001", BusinessType.Food, OrderType.Retail,
            "Cliente", "+525511112222", null, null);
        order.AddItem(Guid.NewGuid(), "Tacos", 2, 25m);
        await using (var context = database.CreateContext())
        {
            context.Tenants.AddRange(tenant, other);
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            // What the previous version inserts meanwhile: a line and a status change with the empty default.
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO OrderItems (Id, TenantId, OrderId, ProductId, ProductName, Quantity, ReturnedQuantity, UnitPrice,
                    TotalPrice, TaxRate, IsTaxExempt, Round)
                VALUES ({Guid.NewGuid()}, {NoBusiness}, {order.Id}, {Guid.NewGuid()}, 'Agua', 1, 0, 15, 15, 16, 0, 1);
                INSERT INTO OrderStatusHistory (Id, TenantId, OrderId, PreviousStatus, NewStatus, ChangedAt)
                VALUES ({Guid.NewGuid()}, {NoBusiness}, {order.Id}, 0, 1, {DateTimeOffset.UtcNow});
                """);
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Single(await context.OrderItems.Where(i => i.OrderId == order.Id).ToListAsync());
            Assert.Single(await context.OrderStatusHistories.Where(h => h.OrderId == order.Id).ToListAsync());
        }

        await using (var context = database.CreateContext())
            Assert.Equal(2, await OrderTenantBackfillService.CompleteAsync(context, CancellationToken.None));

        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal(2, await context.OrderItems.CountAsync(i => i.OrderId == order.Id));
            Assert.Equal(2, await context.OrderStatusHistories.CountAsync(h => h.OrderId == order.Id));
        }

        // Another business sees none of them, and nothing is left without a business.
        await using (var context = database.CreateContext(other.Id))
        {
            Assert.Empty(await context.OrderItems.Where(i => i.OrderId == order.Id).ToListAsync());
            Assert.Empty(await context.OrderStatusHistories.Where(h => h.OrderId == order.Id).ToListAsync());
        }

        await using (var context = database.CreateContext())
        {
            Assert.Equal(0, await context.OrderItems.IgnoreQueryFilters().CountAsync(i => i.TenantId == NoBusiness));
            Assert.Equal(0, await OrderTenantBackfillService.CompleteAsync(context, CancellationToken.None));
        }
    }
}
