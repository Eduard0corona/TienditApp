using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
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
/// AddPlatformOrderIds (Fase 22I) only adds a column that admits nulls, its check and a filtered index, so the version
/// deployed before keeps registering and moving orders of the platforms: its new orders leave the long id empty and its
/// edits keep the one written. Rolling it back stops with error 50032 while a long id is saved (only roll-forward then),
/// and otherwise drops only what it added.
/// </summary>
public class PlatformOrderIdsMigrationSqlServerTests
{
    // The migration right before AddPlatformOrderIds, where its rollback.sql takes the database.
    private const string BeforeLongIds = "20261021040954_AddInventoryTransfers";
    private const string Migration = "20261022011024_AddPlatformOrderIds";

    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 30, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepRegisteringAndMovingPlatformOrders_AndLeaveTheLongIdAlone()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database);
        var tenant = await SeedAsync(database);

        // The version deployed before registers an order of Rappi, as it always did.
        Guid orderId;
        await using (var context = BeforePlatformOrderIds.CreateContext(database, tenant.Id))
        {
            var order = await NewPlatformOrderAsync(context, tenant.Id, "ORD-000001", "BC953");
            await context.SaveChangesAsync();
            orderId = order.Id;
        }

        // This version reads it without a long id, and writes one.
        await using (var context = database.CreateContext(tenant.Id))
        {
            var order = await LoadAsync(context, orderId);
            Assert.Null(order.PlatformDetail!.ExternalOrderId);
            Assert.True(order.ChangePlatformOrderId(PlatformOrderId.FromOptional("1029384756102938475"), DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }

        // The version before still moves the order along; the long id it does not know stays.
        await using (var context = BeforePlatformOrderIds.CreateContext(database, tenant.Id))
        {
            var order = await context.Orders.Include(o => o.PlatformDetail).SingleAsync(o => o.Id == orderId);
            Assert.Equal("BC953", order.PlatformDetail!.ExternalOrderNumber);
            order.TransitionTo(OrderStatus.InPreparation, new FoodOrderStateMachine(), "cocina");
            await context.SaveChangesAsync();
        }
        await using (var context = database.CreateContext(tenant.Id))
        {
            var order = await LoadAsync(context, orderId);
            Assert.Equal(OrderStatus.InPreparation, order.Status);
            Assert.Equal("1029384756102938475", order.PlatformDetail!.ExternalOrderId);
        }
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_WhileALongIdIsSaved_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database);
        var tenant = await SeedAsync(database);
        Guid withLongId;
        await using (var context = database.CreateContext(tenant.Id))
        {
            var order = await NewPlatformOrderAsync(context, tenant.Id, "ORD-000001", "BC953", "c1a2b3d4-0000-4e5f-8a9b-1234567890ab");
            await NewPlatformOrderAsync(context, tenant.Id, "ORD-000002", "BC954");
            await context.SaveChangesAsync();
            withLongId = order.Id;
        }

        // One long id stops the rollback, and nothing changes.
        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeLongIds));
        Assert.Equal(50032, error.Number);
        Assert.Contains("long id of orders of the delivery platforms", error.Message);
        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal("C1A2B3D4-0000-4E5F-8A9B-1234567890AB", (await LoadAsync(context, withLongId)).PlatformDetail!.ExternalOrderId);
        }

        // Once the business clears it, the rollback drops the column, its check and its index, and nothing else.
        await using (var context = database.CreateContext(tenant.Id))
        {
            var order = await LoadAsync(context, withLongId);
            Assert.True(order.ChangePlatformOrderId(null, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }
        await MigrateAsync(database, BeforeLongIds);
        await using (var context = BeforePlatformOrderIds.CreateContext(database, tenant.Id))
        {
            Assert.Equal(0, await CountAsync(context, $"""
                SELECT COUNT(*) AS [Value] FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'dbo.PlatformOrderDetails') AND [name] = N'ExternalOrderId'
                """));
            Assert.Equal(0, await CountAsync(context, $"""
                SELECT COUNT(*) AS [Value] FROM sys.objects WHERE [name] = N'CK_PlatformOrderDetails_ExternalOrderId'
                """));
            Assert.Equal(0, await CountAsync(context, $"""
                SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE [name] = N'IX_PlatformOrderDetails_TenantId_Channel_ExternalOrderId'
                """));
            Assert.Equal(BeforeLongIds, await LatestMigrationAsync(context));
            Assert.Equal(
                new[] { "BC953", "BC954" },
                await context.PlatformOrderDetails.OrderBy(d => d.ExternalOrderNumber).Select(d => d.ExternalOrderNumber).ToListAsync());
        }
        // Rolling back again does nothing.
        await MigrateAsync(database, BeforeLongIds);

        // Forward again (to this migration; later ones apply on top): every order starts without a long id, and one can be
        // written.
        await MigrateAsync(database, Migration);
        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.All(await context.PlatformOrderDetails.ToListAsync(), detail => Assert.Null(detail.ExternalOrderId));
            var order = await LoadAsync(context, withLongId);
            Assert.True(order.ChangePlatformOrderId(PlatformOrderId.FromOptional("C1A2B3D4-0000-4E5F-8A9B-1234567890AB"), DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }
    }

    private static async Task<Tenant> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant("Cocina sin local", slug, BusinessType.Food, $"{slug}@demo.com");
        await using var seed = database.CreateContext();
        seed.AddRange(
            tenant,
            new TenantSettings(tenant.Id, TenantCapability.Food),
            new DeliveryPlatformSetting(tenant.Id, OrderChannel.Rappi, isActive: true, PlatformTerms.WithFiscalDefaults(30m)));
        await seed.SaveChangesAsync();
        return tenant;
    }

    /// <summary>A paquete of $1,000 sold through Rappi, which charged it; added to the context, not saved.</summary>
    private static async Task<Order> NewPlatformOrderAsync(
        ApplicationDbContext context, Guid tenantId, string number, string folio, string? longId = null)
    {
        var setting = await context.DeliveryPlatformSettings.SingleAsync(s => s.Channel == OrderChannel.Rappi);
        var order = new Order(
            tenantId, null, null, true, number, BusinessType.Food, OrderType.Retail, "Juan", null, null, null,
            pricesIncludeTax: true, channel: OrderChannel.Rappi);
        order.AddItem(Guid.NewGuid(), "Paquete familiar", 1, 1000m);
        order.RegisterPlatformOrder(
            setting, PlatformOrderNumber.From(folio), PlatformCollectedBy.Platform, Now, null, PlatformOrderId.FromOptional(longId));
        order.MarkAsCreated();
        context.Orders.Add(order);
        return order;
    }

    private static Task<Order> LoadAsync(ApplicationDbContext context, Guid orderId)
        => context.Orders.Include(o => o.PlatformDetail).SingleAsync(o => o.Id == orderId);

    /// <summary>Applies the migrations up to <paramref name="target"/> (all of them without one), as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string? target = null)
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
