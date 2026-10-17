using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
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
/// AddOrderTrackingToken (Fase 24C, ADR 0023): the orders already stored keep no link; a link has the length the domain
/// gives it and opens one order of the whole platform; the version deployed before keeps creating orders over the new
/// column; and rolling back drops only what it added.
/// </summary>
public class OrderTrackingMigrationSqlServerTests
{
    // The migration right before AddOrderTrackingToken, where its rollback.sql takes the database.
    private const string BeforeTracking = "20261017070332_AddAccountAddresses";
    private const string Migration = "20261017100104_AddOrderTrackingToken";
    private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE";

    [SqlServerFact]
    public async Task OrdersAlreadyStored_ShouldKeepNoLink_AndThePreviousVersionKeepsCreatingOrders()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeTracking);
        var tenant = await SeedTenantAsync(database);
        Guid stored;
        await using (var before = BeforeOrderTracking.CreateContext(database, tenant.Id))
        {
            var order = NewOrder(tenant.Id);
            before.Orders.Add(order);
            await before.SaveChangesAsync();
            stored = order.Id;
        }

        await MigrateAsync(database, Migration);

        // The version deployed before does not know the column: its orders are stored without a link.
        await using (var previous = BeforeOrderTracking.CreateContext(database, tenant.Id))
        {
            previous.Orders.Add(NewOrder(tenant.Id));
            await previous.SaveChangesAsync();
        }
        await using var check = database.CreateContext(tenant.Id);
        var orders = await check.Orders.ToListAsync();
        Assert.Equal(2, orders.Count);
        Assert.All(orders, order => Assert.Null(order.TrackingToken));
        Assert.Contains(orders, order => order.Id == stored);
    }

    [SqlServerFact]
    public async Task Link_ShouldHaveItsLength_AndOpenOneOrderOfThePlatform()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        var other = await SeedTenantAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            var order = NewOrder(tenant.Id);
            order.IssueTrackingToken(Token);
            context.Orders.Add(order);
            await context.SaveChangesAsync();
        }

        var tooShort = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database,
            $"UPDATE [Orders] SET [TrackingToken] = {"corto"} WHERE [TenantId] = {tenant.Id}"));
        Assert.Equal(547, tooShort.Number);
        Assert.Contains("CK_Orders_TrackingToken", tooShort.Message);

        // The same link in another business is still the same link: the index is not per business.
        await using var neighbor = database.CreateContext(other.Id);
        var copy = NewOrder(other.Id);
        copy.IssueTrackingToken(Token);
        neighbor.Orders.Add(copy);
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => neighbor.SaveChangesAsync());
        Assert.Contains("IX_Orders_TrackingToken", duplicate.InnerException!.Message);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldDropOnlyWhatItAdded_AndForwardAgainOrdersHaveNoLink()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            var order = NewOrder(tenant.Id);
            order.IssueTrackingToken(Token);
            context.Orders.Add(order);
            await context.SaveChangesAsync();
        }

        await MigrateAsync(database, BeforeTracking);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeTracking, await LatestMigrationAsync(context));
            Assert.Equal((0, 0, 0, 1), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.Orders') AND [name] = N'TrackingToken'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE [name] = N'IX_Orders_TrackingToken'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE [name] = N'CK_Orders_TrackingToken'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Orders]")));
        }

        // Rolling back again does nothing, and forward again the order is there, without its link.
        await MigrateAsync(database, BeforeTracking);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenant.Id);
        Assert.Null((await forward.Orders.SingleAsync()).TrackingToken);
    }

    private static Order NewOrder(Guid tenantId)
    {
        var order = new Order(
            tenantId, null, null, createdByBusiness: true, $"A-{Guid.NewGuid():N}"[..10], BusinessType.Food,
            OrderType.Retail, "Valeria", customerPhone: "+525522107765", customerNotes: null, tableNumber: null,
            pricesIncludeTax: true, channel: OrderChannel.Online);
        order.AddItem(Guid.NewGuid(), "Café americano", 1, 35m);
        return order;
    }

    private static async Task<Tenant> SeedTenantAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Negocio {slug}", slug, BusinessType.Food, $"{slug}@demo.com");
        await using var seed = BeforeOrderTracking.CreateContext(database);
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food));
        await seed.SaveChangesAsync();
        return tenant;
    }

    /// <summary>Applies the migrations up to <paramref name="target"/>, as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string target)
    {
        await using var context = database.CreateContext();
        await context.GetService<IMigrator>().MigrateAsync(target);
    }

    private static async Task ExecuteAsync(SqlServerTestDatabase database, FormattableString sql)
    {
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
