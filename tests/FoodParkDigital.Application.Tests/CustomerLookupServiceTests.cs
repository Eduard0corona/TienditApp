using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

public class CustomerLookupServiceTests
{
    [Fact]
    public async Task CreateOrder_ShouldReuseConcurrentTemporaryCustomer_WhenItsOwnInsertLosesTheRace()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var (tenantId, productId) = await SeedCatalogAsync(host);
        host.SetTenant(tenantId);

        // Another request registers the same tenant/phone after this lookup found nothing and
        // before it inserts, so the lookup's insert hits the (TenantId, PhoneNormalized) unique index.
        var winnerId = Guid.Empty;
        host.BeforeSave.Arm(
            when: context => context.ChangeTracker.Entries<TemporaryCustomer>().Any(e => e.State == EntityState.Added),
            action: _ => host.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<ApplicationDbContext>();
                var winner = new TemporaryCustomer(tenantId, "+525522223333", "Cliente Ganador");
                db.TemporaryCustomers.Add(winner);
                await db.SaveChangesAsync();
                winnerId = winner.Id;
            }));

        var result = await host.HandleAsync<CreateOrderCommandHandler, Result<OrderDto>>(handler => handler.Handle(
            new CreateOrderCommand
            {
                CustomerPhone = "55 2222 3333",
                CustomerName = "Cliente Perdedor",
                Items = [new CreateOrderItemDto { ProductId = productId, Quantity = 1 }]
            },
            CancellationToken.None));

        Assert.True(host.BeforeSave.Fired);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(winnerId, result.Value.TemporaryCustomerId);

        var stored = await host.QueryAsync(async db => new
        {
            Temps = await db.TemporaryCustomers.IgnoreQueryFilters().ToListAsync(),
            Orders = await db.Orders.IgnoreQueryFilters().ToListAsync()
        });
        var temp = Assert.Single(stored.Temps);
        Assert.Equal(winnerId, temp.Id);
        Assert.Equal("Cliente Ganador", temp.Name);
        Assert.Equal(winnerId, Assert.Single(stored.Orders).TemporaryCustomerId);
    }

    private static Task<(Guid TenantId, Guid ProductId)> SeedCatalogAsync(SqliteTestHost host)
        => host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();

            var tenant = new Tenant("Demo Tenant", "demo-tenant", BusinessType.Food, "demo@tenant.com");
            var category = new Category(tenant.Id, "General", null);
            var product = new Product(tenant.Id, category.Id, "Producto Demo", null, 100m, "MXN");

            db.Tenants.Add(tenant);
            db.Categories.Add(category);
            db.Products.Add(product);
            await db.SaveChangesAsync();

            return (tenant.Id, product.Id);
        });
}
