using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// AddCustomerCredit (Fase 26A, ADR 0033): how much a business lets each customer owe, to the centavo, and the counter
/// sales closed on credit; what the deployed version stores meanwhile takes no limit and no credit; and rolling back stops
/// with error 50026 once a business gave credit or sold on it, and otherwise drops only the two columns and the index.
/// </summary>
public class CustomerCreditMigrationSqlServerTests
{
    // The migration right before AddCustomerCredit, where its rollback.sql takes the database.
    private const string BeforeCredit = "20261019150436_AddOnlineBookingStaff";
    private const string Migration = "20261019202512_AddCustomerCredit";
    private const string Phone = "+525522107765";

    [SqlServerFact]
    public async Task Columns_ShouldKeepTheLimitToTheCentavo_AndTheSaleOnCredit_WhileThePreviousVersionStoresNeither()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeCredit);
        var tenant = await SeedTenantAsync(database);
        await using (var before = BeforeCustomerCredit.CreateContext(database, tenant.Id))
        {
            before.Customers.Add(new Customer(tenant.Id, "Valeria", Phone, null));
            before.Orders.Add(NewCounterSale(tenant.Id));
            await before.SaveChangesAsync();
        }

        await MigrateAsync(database, Migration);

        // The version deployed before does not know the columns: what it stores has no limit and is not on credit.
        await using (var previous = BeforeCustomerCredit.CreateContext(database, tenant.Id))
        {
            previous.Customers.Add(new Customer(tenant.Id, "Mario", "+525522107766", null));
            previous.Orders.Add(NewCounterSale(tenant.Id));
            await previous.SaveChangesAsync();
        }
        await using (var context = BeforeCustomerWriteOffs.CreateContext(database, tenant.Id))
        {
            Assert.All(await context.Customers.ToListAsync(), customer => Assert.Null(customer.CreditLimit));
            Assert.All(await context.Orders.ToListAsync(), order => Assert.False(order.SoldOnCredit));

            (await context.Customers.SingleAsync(c => c.Phone == Phone)).SetCreditLimit(1500.50m);
            var sale = NewCounterSale(tenant.Id);
            sale.SellOnCredit();
            context.Orders.Add(sale);
            await context.SaveChangesAsync();
        }

        await using var check = BeforeCustomerWriteOffs.CreateContext(database, tenant.Id);
        Assert.Equal(1500.50m, (await check.Customers.SingleAsync(c => c.Phone == Phone)).CreditLimit);
        Assert.Equal(1, await check.Orders.CountAsync(o => o.SoldOnCredit));
        Assert.Equal(1, await CountAsync(check, $"""
            SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'dbo.Orders') AND [name] = N'IX_Orders_TenantId_CustomerPhone'
            """));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceABusinessGaveCreditOrSoldOnIt_AndThenDropOnlyTheColumns()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        await using (var context = BeforeCustomerWriteOffs.CreateContext(database, tenant.Id))
        {
            var customer = new Customer(tenant.Id, "Valeria", Phone, null);
            customer.SetCreditLimit(800m);
            context.Customers.Add(customer);
            await context.SaveChangesAsync();
        }

        // A limit alone stops it.
        var limit = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeCredit));
        Assert.Equal(50026, limit.Number);
        Assert.Contains("sell on credit", limit.Message);

        // And a sale on credit alone too.
        await using (var context = BeforeCustomerWriteOffs.CreateContext(database, tenant.Id))
        {
            (await context.Customers.SingleAsync()).SetCreditLimit(null);
            var sale = NewCounterSale(tenant.Id);
            sale.SellOnCredit();
            context.Orders.Add(sale);
            await context.SaveChangesAsync();
        }
        var sold = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeCredit));
        Assert.Equal(50026, sold.Number);
        await using (var context = BeforeCustomerWriteOffs.CreateContext(database))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            await context.Database.ExecuteSqlRawAsync("UPDATE [Orders] SET [SoldOnCredit] = 0");
        }

        await MigrateAsync(database, BeforeCredit);
        await using (var context = BeforeCustomerWriteOffs.CreateContext(database))
        {
            Assert.Equal(BeforeCredit, await LatestMigrationAsync(context));
            Assert.Equal((0, 0, 0, 1, 1), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.Customers') AND [name] = N'CreditLimit'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.Orders') AND [name] = N'SoldOnCredit'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE [name] = N'IX_Orders_TenantId_CustomerPhone'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Customers]"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Orders] WHERE [Total] > [PaidAmount]")));
        }

        // Rolling back again does nothing, and forward again the customer and the sale are there, without credit.
        await MigrateAsync(database, BeforeCredit);
        await MigrateAsync(database, Migration);
        await using var forward = BeforeCustomerWriteOffs.CreateContext(database, tenant.Id);
        Assert.Null((await forward.Customers.SingleAsync()).CreditLimit);
        Assert.False((await forward.Orders.SingleAsync()).SoldOnCredit);
    }

    /// <summary>A counter sale left with a balance, as «Guardar sin cobrar» stores it.</summary>
    private static Order NewCounterSale(Guid tenantId)
    {
        var order = new Order(
            tenantId, null, null, createdByBusiness: true, $"A-{Guid.NewGuid():N}"[..10], BusinessType.Retail,
            OrderType.Retail, "Valeria", customerPhone: Phone, customerNotes: null, tableNumber: null);
        order.AddItem(Guid.NewGuid(), "Jabón", 2, 35m);
        order.TransitionTo(OrderStatus.Confirmed, new RetailOrderStateMachine(), "cajero");
        return order;
    }

    private static async Task<Tenant> SeedTenantAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Tienda {slug}", slug, BusinessType.Retail, $"{slug}@demo.com");
        await using var seed = BeforeCustomerCredit.CreateContext(database);
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Retail));
        await seed.SaveChangesAsync();
        return tenant;
    }

    /// <summary>Applies the migrations up to <paramref name="target"/>, as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
