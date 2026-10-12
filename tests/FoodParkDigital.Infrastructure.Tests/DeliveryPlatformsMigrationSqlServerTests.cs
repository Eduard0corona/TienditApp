using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Finance.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Reporting.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Tests.Support;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// AddDeliveryPlatformOrders (Fase 22F) only adds: two tables and DailySalesSnapshots.PlatformAmount with 0 by
/// default, so the version deployed before keeps storing its snapshots. Rolling it back stops with error 50008 once
/// there is an order or payment of a platform (only roll-forward then), and otherwise drops only what it added.
/// </summary>
public class DeliveryPlatformsMigrationSqlServerTests
{
    // The migration right before AddDeliveryPlatformOrders, where its rollback.sql takes the database.
    private const string BeforePlatforms = "20261011161835_AddSpecialDays";
    private const string Migration = "20261012005540_AddDeliveryPlatformOrders";

    private static readonly DateTimeOffset Now = new(2026, 10, 12, 18, 30, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task PreviousVersion_ShouldStoreItsSnapshotsWithoutPlatformAmount_WhichTakesZero()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database);
        var tenant = await SeedAsync(database);

        // The version deployed before does not know the column: its snapshot of a day takes the default.
        await using (var context = CreateVersionBeforePlatforms(database, tenant.Id))
        {
            context.DailySalesSnapshots.Add(new DailySalesSnapshot(
                tenant.Id, new DateOnly(2026, 10, 11), null, OrderType.Retail, "Central Standard Time (Mexico)",
                new DailySalesFigures(1, 1, 1, 120m, 120m, 0m, 0m, 0m, 0m, 0, 0m, PlatformAmount: 999m), Now));
            await context.SaveChangesAsync();
            Assert.Equal(120m, (await context.DailySalesSnapshots.AsNoTracking().SingleAsync()).CashAmount);
        }

        await using (var context = database.CreateContext(tenant.Id))
        {
            var stored = await context.DailySalesSnapshots.AsNoTracking().SingleAsync();
            Assert.Equal(0m, stored.PlatformAmount);
            Assert.Equal(120m, stored.PaymentsAmount);

            // This version writes it.
            context.DailySalesSnapshots.Add(new DailySalesSnapshot(
                tenant.Id, new DateOnly(2026, 10, 12), null, OrderType.Retail, "Central Standard Time (Mexico)",
                new DailySalesFigures(1, 1, 1, 350m, 0m, 0m, 0m, 0m, 0m, 0, 0m, PlatformAmount: 350m), Now));
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext(tenant.Id))
            Assert.Equal(
                new[] { 0m, 350m },
                await context.DailySalesSnapshots.AsNoTracking().OrderBy(s => s.BusinessDate).Select(s => s.PlatformAmount).ToListAsync());
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_WhileThereArePlatformOrdersOrPayments_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database);
        var tenant = await SeedAsync(database);
        Guid platformOrderId;
        Guid counterOrderId;
        await using (var context = database.CreateContext(tenant.Id))
        {
            var setting = new DeliveryPlatformSetting(tenant.Id, OrderChannel.Rappi, isActive: true, PlatformTerms.WithFiscalDefaults(30m));
            context.DeliveryPlatformSettings.Add(setting);
            var platformOrder = NewOrder(tenant.Id, "ORD-000001", OrderChannel.Rappi);
            platformOrder.RegisterPlatformOrder(setting, PlatformOrderNumber.From("BC953"), PlatformCollectedBy.Platform, Now, null);
            var counterOrder = NewOrder(tenant.Id, "ORD-000002", OrderChannel.Counter);
            counterOrder.RegisterPayment(counterOrder.Total, OrderPaymentMethod.Cash, Now, null, null, null, null);
            counterOrder.RegisterRefund(100m, OrderPaymentMethod.Cash, "Devolución", Now, "cajero", null, null, false, []);
            var session = new CashSession(tenant.Id, Now.AddHours(-2), "cajero", 500m);
            session.RegisterTransaction(CashTransactionType.SalePayment, counterOrder.Total, Now, OrderPaymentMethod.Cash, orderId: counterOrder.Id);
            context.AddRange(platformOrder, counterOrder, session);
            context.DailySalesSnapshots.Add(new DailySalesSnapshot(
                tenant.Id, new DateOnly(2026, 10, 11), null, OrderType.Retail, "Central Standard Time (Mexico)",
                new DailySalesFigures(1, 1, 1, 1000m, 0m, 0m, 0m, 0m, 0m, 0, 0m, PlatformAmount: 1000m), Now));
            await context.SaveChangesAsync();
            platformOrderId = platformOrder.Id;
            counterOrderId = counterOrder.Id;
        }

        // An order of Rappi, charged by Rappi: its channel and its payment stop the rollback, and nothing changes.
        await AssertRollbackStoppedAsync(database, tenant.Id);
        await using (var context = database.CreateContext(tenant.Id))
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM OrderPayments WHERE OrderId = {platformOrderId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Orders WHERE Id = {platformOrderId}");
        }

        // Each of the other rows the previous code would not understand stops it on its own.
        FormattableString[] poisons =
        [
            $"UPDATE Orders SET Channel = 5 WHERE Id = {counterOrderId}",
            $"UPDATE OrderPayments SET Method = 5 WHERE OrderId = {counterOrderId}",
            $"UPDATE OrderRefunds SET Method = 5 WHERE OrderId = {counterOrderId}",
            $"UPDATE CashTransactions SET PaymentMethod = 5 WHERE OrderId = {counterOrderId}"
        ];
        FormattableString clean = $"""
            UPDATE Orders SET Channel = 0 WHERE Id = {counterOrderId};
            UPDATE OrderPayments SET Method = 0 WHERE OrderId = {counterOrderId};
            UPDATE OrderRefunds SET Method = 0 WHERE OrderId = {counterOrderId};
            UPDATE CashTransactions SET PaymentMethod = 0 WHERE OrderId = {counterOrderId};
            """;
        foreach (var poison in poisons)
        {
            await ExecuteAsync(database, poison);
            await AssertRollbackStoppedAsync(database, tenant.Id);
            await ExecuteAsync(database, clean);
        }

        // Without them, the rollback drops the tables (the settings with them) and the column, and nothing else.
        await MigrateAsync(database, BeforePlatforms);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(0, await CountAsync(context, $"""
                SELECT COUNT(*) AS [Value] FROM sys.objects WHERE [name] IN (N'PlatformOrderDetails', N'DeliveryPlatformSettings')
                """));
            Assert.Equal(0, await CountAsync(context, $"""
                SELECT COUNT(*) AS [Value] FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'dbo.DailySalesSnapshots') AND [name] = N'PlatformAmount'
                """));
            Assert.Equal(BeforePlatforms, await LatestMigrationAsync(context));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM Orders"));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM DailySalesSnapshots"));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM CashTransactions"));
        }
        // Rolling back again does nothing.
        await MigrateAsync(database, BeforePlatforms);

        // Forward again: the tables start empty and the stored day takes 0.
        await MigrateAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Empty(await context.DeliveryPlatformSettings.ToListAsync());
            Assert.Empty(await context.PlatformOrderDetails.ToListAsync());
            Assert.Equal(0m, (await context.DailySalesSnapshots.AsNoTracking().SingleAsync()).PlatformAmount);
            Assert.Equal(Migration, await LatestMigrationAsync(context));
        }
    }

    private static async Task AssertRollbackStoppedAsync(SqlServerTestDatabase database, Guid tenantId)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforePlatforms));
        Assert.Equal(50008, error.Number);
        Assert.Contains("delivery platform orders or payments", error.Message);

        await using var context = database.CreateContext(tenantId);
        Assert.Equal(Migration, await LatestMigrationAsync(context));
        Assert.Equal(2, await CountAsync(context, $"""
            SELECT COUNT(*) AS [Value] FROM sys.objects WHERE [name] IN (N'PlatformOrderDetails', N'DeliveryPlatformSettings')
            """));
        Assert.Single(await context.DeliveryPlatformSettings.AsNoTracking().ToListAsync());
        Assert.Equal(1000m, (await context.DailySalesSnapshots.AsNoTracking().SingleAsync()).PlatformAmount);
    }

    private static async Task<Tenant> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant("Cocina sin local", slug, BusinessType.Food, $"{slug}@demo.com");
        await using var seed = database.CreateContext();
        seed.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food));
        await seed.SaveChangesAsync();
        return tenant;
    }

    private static Order NewOrder(Guid tenantId, string number, OrderChannel channel)
    {
        var order = new Order(
            tenantId, null, null, true, number, BusinessType.Food, OrderType.Retail, "Juan", null, null, null,
            pricesIncludeTax: true, channel: channel);
        order.AddItem(Guid.NewGuid(), "Paquete familiar", 1, 1000m);
        return order;
    }

    private static VersionBeforePlatformsContext CreateVersionBeforePlatforms(SqlServerTestDatabase database, Guid tenantId)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new VersionBeforePlatformsContext(options, tenant, mediator));

    /// <summary>The app as deployed before Fase 22F: no platform tables and no PlatformAmount.</summary>
    private sealed class VersionBeforePlatformsContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentTenantService tenantService,
        IMediator mediator) : ApplicationDbContext(options, tenantService, mediator)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Order>().Ignore(o => o.PlatformDetail);
            modelBuilder.Ignore<PlatformOrderDetail>();
            modelBuilder.Ignore<DeliveryPlatformSetting>();
            modelBuilder.Entity<DailySalesSnapshot>().Ignore(s => s.PlatformAmount);
        }
    }

    /// <summary>Applies the migrations up to <paramref name="target"/> (all of them without one), as a deployment does.</summary>
    private static async Task MigrateAsync(SqlServerTestDatabase database, string? target = null)
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
