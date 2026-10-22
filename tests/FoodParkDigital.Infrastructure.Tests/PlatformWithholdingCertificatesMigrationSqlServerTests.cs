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
/// AddPlatformWithholdingCertificates (Fase 22J) only adds a table, so the version deployed before keeps registering and
/// moving orders of the platforms without knowing it. Rolling it back stops with error 50033 while any CFDI de retenciones
/// is saved, a removed one included (only roll-forward then), and otherwise drops only the table.
/// </summary>
public class PlatformWithholdingCertificatesMigrationSqlServerTests
{
    // The migration right before AddPlatformWithholdingCertificates, where its rollback.sql takes the database.
    private const string BeforeCertificates = "20261022011024_AddPlatformOrderIds";
    private const string Migration = "20261022033401_AddPlatformWithholdingCertificates";

    private static readonly DateOnly CurrentMonth = new(2026, 10, 1);
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 30, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepRegisteringAndMovingPlatformOrders_NextToTheCertificates()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database);
        var tenant = await SeedAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            context.PlatformWithholdingCertificates.Add(NewCertificate(tenant.Id));
            await context.SaveChangesAsync();
        }

        // The version deployed before registers an order of Rappi and moves it along, as it always did.
        Guid orderId;
        await using (var context = BeforePlatformWithholdingCertificates.CreateContext(database, tenant.Id))
        {
            var order = await NewPlatformOrderAsync(context, tenant.Id, "ORD-000001", "BC953");
            await context.SaveChangesAsync();
            orderId = order.Id;
        }
        await using (var context = BeforePlatformWithholdingCertificates.CreateContext(database, tenant.Id))
        {
            var order = await context.Orders.Include(o => o.PlatformDetail).SingleAsync(o => o.Id == orderId);
            order.TransitionTo(OrderStatus.InPreparation, new FoodOrderStateMachine(), "cocina");
            await context.SaveChangesAsync();
        }

        // This version reads the order and the CFDI it does not touch.
        await using (var context = database.CreateContext(tenant.Id))
        {
            var order = await context.Orders.Include(o => o.PlatformDetail).SingleAsync(o => o.Id == orderId);
            Assert.Equal((OrderStatus.InPreparation, "BC953"), (order.Status, order.PlatformDetail!.ExternalOrderNumber));
            Assert.Equal(1708.32m, (await context.PlatformWithholdingCertificates.SingleAsync()).TotalWithheld);
        }
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_WhileACertificateIsSaved_AndThenDropOnlyTheTable()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database);
        var tenant = await SeedAsync(database);
        await using (var context = database.CreateContext(tenant.Id))
        {
            await NewPlatformOrderAsync(context, tenant.Id, "ORD-000001", "BC953");
            var certificate = NewCertificate(tenant.Id);
            // A removed one is still the business's record: it stops the rollback too.
            certificate.Remove("Era de otra sucursal", Now);
            context.PlatformWithholdingCertificates.Add(certificate);
            await context.SaveChangesAsync();
        }

        // One CFDI stops the rollback, and nothing changes.
        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeCertificates));
        Assert.Equal(50033, error.Number);
        Assert.Contains("CFDI de retenciones of delivery platforms", error.Message);
        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal("Era de otra sucursal", (await context.PlatformWithholdingCertificates.SingleAsync()).RemovalReason);
        }

        // Once its rows are exported and deleted, the rollback drops the table, and nothing else.
        await using (var context = database.CreateContext(tenant.Id))
        {
            await context.Database.ExecuteSqlRawAsync("DELETE FROM [PlatformWithholdingCertificates]");
        }
        await MigrateAsync(database, BeforeCertificates);
        await using (var context = BeforePlatformWithholdingCertificates.CreateContext(database, tenant.Id))
        {
            Assert.Equal(0, await CountAsync(context, $"""
                SELECT COUNT(*) AS [Value] FROM sys.objects
                WHERE [name] LIKE N'%PlatformWithholdingCertificates%'
                """));
            Assert.Equal(BeforeCertificates, await LatestMigrationAsync(context));
            Assert.Equal("BC953", (await context.PlatformOrderDetails.SingleAsync()).ExternalOrderNumber);
        }
        // Rolling back again does nothing.
        await MigrateAsync(database, BeforeCertificates);

        // Forward again (to this migration; later ones apply on top): the table starts empty and takes a CFDI.
        await MigrateAsync(database, Migration);
        await using (var context = database.CreateContext(tenant.Id))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Empty(await context.PlatformWithholdingCertificates.ToListAsync());
            context.PlatformWithholdingCertificates.Add(NewCertificate(tenant.Id));
            await context.SaveChangesAsync();
        }
    }

    private static PlatformWithholdingCertificate NewCertificate(Guid tenantId)
        => new(tenantId, OrderChannel.Rappi, PlatformWithholdingCertificatesSqlServerTests.Data(), CurrentMonth);

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
    private static async Task<Order> NewPlatformOrderAsync(ApplicationDbContext context, Guid tenantId, string number, string folio)
    {
        var setting = await context.DeliveryPlatformSettings.SingleAsync(s => s.Channel == OrderChannel.Rappi);
        var order = new Order(
            tenantId, null, null, true, number, BusinessType.Food, OrderType.Retail, "Juan", null, null, null,
            pricesIncludeTax: true, channel: OrderChannel.Rappi);
        order.AddItem(Guid.NewGuid(), "Paquete familiar", 1, 1000m);
        order.RegisterPlatformOrder(setting, PlatformOrderNumber.From(folio), PlatformCollectedBy.Platform, Now, null);
        order.MarkAsCreated();
        context.Orders.Add(order);
        return order;
    }

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
