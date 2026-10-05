using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// CK_Orders_CustomerLink, evaluated by SQLite with the same SQL the migration creates on SQL Server:
/// a counter sale may have no customer, a customer order may not, and no order has both links.
/// </summary>
public class OrderCustomerLinkConstraintTests
{
    [Fact]
    public async Task WalkInSale_ShouldBeStoredWithoutCustomer()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var tenantId = await SeedTenantAsync(host);

        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.Orders.Add(new Order(tenantId, null, null, true, "ORD-000001", BusinessType.Food, OrderType.Retail,
                "Público en general", null, null, "Mesa 1"));
            await db.SaveChangesAsync();
        });

        var stored = await host.QueryAsync(db => db.Orders.IgnoreQueryFilters().SingleAsync());
        Assert.Null(stored.CustomerUserId);
        Assert.Null(stored.TemporaryCustomerId);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    public async Task Constraint_ShouldRejectInvalidLinks_EvenWhenTheDomainIsBypassed(
        bool createdByBusiness,
        bool withUser,
        bool withTemporary)
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var tenantId = await SeedTenantAsync(host);
        var userId = withUser ? await SeedUserAsync(host) : (Guid?)null;
        var temporaryId = withTemporary ? await SeedTemporaryCustomerAsync(host, tenantId) : (Guid?)null;

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            // A valid order rewritten after construction: only the database rule is left to stop it.
            var order = new Order(tenantId, null, null, true, "ORD-000002", BusinessType.Food, OrderType.Retail,
                "Cliente", null, null, null);
            db.Orders.Add(order);
            db.Entry(order).Property(o => o.CreatedByBusiness).CurrentValue = createdByBusiness;
            db.Entry(order).Property(o => o.CustomerUserId).CurrentValue = userId;
            db.Entry(order).Property(o => o.TemporaryCustomerId).CurrentValue = temporaryId;
            await db.SaveChangesAsync();
        }));
        Assert.Contains("CK_Orders_CustomerLink", error.InnerException?.Message);
    }

    private static Task<Guid> SeedTenantAsync(SqliteTestHost host) => host.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var tenant = new Tenant("Café", "cafe", BusinessType.Food, "cafe@demo.com");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        host.SetTenant(tenant.Id);
        return tenant.Id;
    });

    private static Task<Guid> SeedUserAsync(SqliteTestHost host) => host.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var user = new Domain.Identity.Entities.ApplicationUser
        {
            UserName = "cliente@demo.com",
            Email = "cliente@demo.com",
            FirstName = "Cliente",
            LastName = "Demo",
            Role = Domain.Identity.Enums.UserRole.Customer
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    });

    private static Task<Guid> SeedTemporaryCustomerAsync(SqliteTestHost host, Guid tenantId) => host.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var temporary = new Domain.Customers.Entities.TemporaryCustomer(tenantId, "+525511112222", "Invitado");
        db.TemporaryCustomers.Add(temporary);
        await db.SaveChangesAsync();
        return temporary.Id;
    });
}
