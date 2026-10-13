using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
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
/// AddPlatformSettlements (Fase 22G) only adds three tables, so the version deployed before keeps registering and
/// refunding orders over them, settled ones included. Rolling it back stops with error 50010 while there is a settlement
/// saved, of any status (no activity entry keeps its orders or adjustments), and otherwise drops only what it added.
/// </summary>
public class PlatformSettlementsMigrationSqlServerTests
{
    // The migration right before AddPlatformSettlements, where its rollback.sql takes the database.
    private const string BeforeSettlements = "20261012112823_AddPlatformPrices";
    private const string Migration = "20261012145908_AddPlatformSettlements";
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 18, 30, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepRefundingASettledOrder_AndTheSettlementStays()
    {
        // The base as it is once every migration is applied: the version before refunds over tables it does not know.
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var tenant = await SeedAsync(database);
        var orderId = await SeedOrderAsync(database, tenant.Id);
        await SettleAsync(database, tenant.Id, orderId, close: true);

        // The version deployed before refunds the order the platform cancelled late, as it always did.
        await using (var context = CreateVersionBeforeSettlements(database, tenant.Id))
        {
            var order = await context.Orders.Include(o => o.Payments).Include(o => o.Refunds).SingleAsync(o => o.Id == orderId);
            order.RegisterRefund(1000m, OrderPaymentMethod.Platform, "La plataforma lo canceló", Now, "duena@demo.com", null, null, false, []);
            await context.SaveChangesAsync();
        }

        await using var check = database.CreateContext(tenant.Id);
        Assert.Equal(1000m, (await check.Orders.SingleAsync()).RefundedAmount);
        var line = await check.PlatformSettlementOrders.SingleAsync();
        Assert.Equal((orderId, 561.48m, true), (line.OrderId, line.ExpectedNet, line.IsActive));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_WhileThereAreSettlements_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedAsync(database);
        var orderId = await SeedOrderAsync(database, tenant.Id);
        var settlementId = await SettleAsync(database, tenant.Id, orderId, close: false);

        // An open settlement stops the rollback, and so does a voided one: nothing changes.
        await AssertRollbackStoppedAsync(database, settlements: 1);
        await using (var context = database.CreateContext(tenant.Id))
        {
            (await context.PlatformSettlements.Include(s => s.Orders).Include(s => s.Adjustments).SingleAsync()).Void("De prueba", Now);
            await context.SaveChangesAsync();
        }
        await AssertRollbackStoppedAsync(database, settlements: 1);
        await ExecuteAsync(database, $"DELETE FROM PlatformSettlements WHERE Id = {settlementId}");

        // Without settlements, the rollback drops the three tables and nothing else.
        await MigrateAsync(database, BeforeSettlements);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(0, await CountAsync(context, $"""
                SELECT COUNT(*) AS [Value] FROM sys.objects
                WHERE [name] IN (N'PlatformSettlements', N'PlatformSettlementOrders', N'PlatformSettlementAdjustments')
                """));
            Assert.Equal(BeforeSettlements, await LatestMigrationAsync(context));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM Orders"));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM PlatformOrderDetails"));
        }
        // Rolling back again does nothing.
        await MigrateAsync(database, BeforeSettlements);

        // Forward again (to this migration; later ones apply on top): the tables start empty, the order pending.
        await MigrateAsync(database, Migration);
        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Empty(await context.PlatformSettlements.ToListAsync());
            Assert.Empty(await context.PlatformSettlementOrders.ToListAsync());
            Assert.Equal(Migration, await LatestMigrationAsync(context));
        }
    }

    private static async Task AssertRollbackStoppedAsync(SqlServerTestDatabase database, int settlements)
    {
        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeSettlements));
        Assert.Equal(50010, error.Number);
        Assert.Contains("settlements of delivery platforms", error.Message);

        await using var context = database.CreateContext();
        Assert.Equal(Migration, await LatestMigrationAsync(context));
        Assert.Equal(settlements, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM PlatformSettlements"));
        Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM PlatformSettlementOrders"));
        Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM PlatformSettlementAdjustments"));
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

    /// <summary>An order of Rappi of $1,000 charged by the platform: 30 % of commission on the total, expected net 561.48.</summary>
    private static async Task<Guid> SeedOrderAsync(SqlServerTestDatabase database, Guid tenantId)
    {
        await using var context = database.CreateContext(tenantId);
        var setting = new DeliveryPlatformSetting(
            tenantId, OrderChannel.Rappi, isActive: true, new PlatformTerms(30m, PlatformCommissionBase.TotalWithTax, 16m, 2.5m, 50m));
        var order = new Order(
            tenantId, null, null, true, "ORD-000001", BusinessType.Food, OrderType.Retail, "Juan", null, null, null,
            pricesIncludeTax: true, channel: OrderChannel.Rappi);
        order.AddItem(Guid.NewGuid(), "Paquete familiar", 1, 1000m);
        order.RegisterPlatformOrder(setting, PlatformOrderNumber.From("BC953"), PlatformCollectedBy.Platform, Now, null);
        order.MarkAsCreated();
        context.AddRange(setting, order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    /// <summary>The deposit of the week of the order, with a claim of $1.48 charged back.</summary>
    private static async Task<Guid> SettleAsync(SqlServerTestDatabase database, Guid tenantId, Guid orderId, bool close)
    {
        await using var context = database.CreateContext(tenantId);
        var settlement = new PlatformSettlement(
            tenantId,
            OrderChannel.Rappi,
            new PlatformSettlementDeposit(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 13), 560m, null, null),
            [new PlatformSettlementOrderInput(orderId, OrderChannel.Rappi, new DateOnly(2026, 10, 7), 561.48m)],
            [new PlatformSettlementAdjustmentInput(PlatformSettlementAdjustmentKind.Claim, null, -1.48m)],
            new DateOnly(2026, 10, 14));
        if (close)
            settlement.Close(Now);
        context.PlatformSettlements.Add(settlement);
        await context.SaveChangesAsync();
        return settlement.Id;
    }

    private static VersionBeforeSettlementsContext CreateVersionBeforeSettlements(SqlServerTestDatabase database, Guid tenantId)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new VersionBeforeSettlementsContext(options, tenant, mediator));

    /// <summary>The app as deployed before Fase 22G: no settlements.</summary>
    private sealed class VersionBeforeSettlementsContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentTenantService tenantService,
        IMediator mediator) : ApplicationDbContext(options, tenantService, mediator)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<OrderDelivery>();
            modelBuilder.Ignore<PlatformSettlementOrder>();
            modelBuilder.Ignore<PlatformSettlementAdjustment>();
            modelBuilder.Ignore<PlatformSettlement>();
        }
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
