using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Orders.StateMachines;
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
/// The table of the orders sent to an address (Fase 22C): one delivery per order, in its business, always loaded with the
/// order so a change of the order keeps its fee in the total, the checks keep every row one the domain could write, and
/// rolling back stops while deliveries are saved and otherwise drops only the table.
/// </summary>
public class OrderDeliveriesSqlServerTests
{
    private const string BeforeOrderDeliveries = "20261013002425_AddDeliveryZones";
    private const string Migration = "20261013042014_AddOrderDeliveries";
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 19, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Table_ShouldHaveOneDeliveryPerOrder_AndGoAwayWithIt()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await using var context = database.CreateContext();

        var unique = await context.Database.SqlQuery<bool>($"""
            SELECT CAST([is_unique] AS bit) AS [Value] FROM sys.indexes WHERE [name] = N'IX_OrderDeliveries_OrderId'
            """).SingleAsync();
        var onDelete = await context.Database.SqlQuery<string>($"""
            SELECT [delete_referential_action_desc] AS [Value] FROM sys.foreign_keys WHERE [name] = N'FK_OrderDeliveries_Orders_OrderId'
            """).SingleAsync();

        Assert.True(unique);
        Assert.Equal("CASCADE", onDelete);
    }

    [SqlServerFact]
    public async Task Order_ShouldAlwaysComeWithItsDelivery_SoAChangeKeepsTheFeeInTheTotal()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, other) = await SeedAsync(database);
        var orderId = await SeedDeliveryOrderAsync(database, tenant.Id);

        // Read as any command reads an order, without asking for its delivery, and charged.
        await using (var context = database.CreateContext(tenant.Id))
        {
            var order = await context.Orders.Include(o => o.Payments).SingleAsync(o => o.Id == orderId);
            Assert.NotNull(order.Delivery);
            order.RegisterPayment(order.Total, OrderPaymentMethod.Cash, Now, null, null, null, null);
            await context.SaveChangesAsync();
        }

        await using (var check = database.CreateContext(tenant.Id))
        {
            var order = await check.Orders.SingleAsync(o => o.Id == orderId);
            var delivery = order.Delivery!;
            Assert.Equal(("72000", "Av. Juárez 123, Col. Centro", "Portón negro"), (delivery.PostalCode, delivery.AddressLine, delivery.References));
            Assert.Equal(("Centro", 35m, 16m), (delivery.ZoneName, delivery.Fee, delivery.FeeTaxRate));
            Assert.Equal((135m, 135m), (order.Total, order.PaidAmount));
        }

        await using var neighbor = database.CreateContext(other.Id);
        Assert.Empty(await neighbor.OrderDeliveries.ToListAsync());
    }

    [SqlServerFact]
    public async Task Shipment_ShouldBeSaved_WithTheOrder()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var orderId = await SeedDeliveryOrderAsync(database, tenant.Id);

        await using (var context = database.CreateContext(tenant.Id))
        {
            var order = await context.Orders.SingleAsync(o => o.Id == orderId);
            order.RegisterCourierShipment(
                CourierShipment.From(DeliveryCourier.Paquetenvia, "pq 1234", "https://paquetenvia.mx/rastreo/PQ1234"), Now, "caja");
            await context.SaveChangesAsync();
        }

        await using var check = database.CreateContext(tenant.Id);
        var delivery = await check.OrderDeliveries.SingleAsync();
        Assert.Equal((DeliveryCourier.Paquetenvia, "PQ1234", "https://paquetenvia.mx/rastreo/PQ1234", "caja", Now),
            (delivery.Courier, delivery.CourierFolio, delivery.TrackingUrl, delivery.ShipmentRegisteredBy, delivery.ShipmentRegisteredAt));
    }

    [SqlServerFact]
    public async Task Shipment_ShouldConflict_WithACancellationSavedMeanwhile()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var orderId = await SeedDeliveryOrderAsync(database, tenant.Id);
        await using var context = database.CreateContext(tenant.Id);
        var order = await context.Orders.SingleAsync(o => o.Id == orderId);

        // Someone cancels the order after this request read it, and before it writes the trip down.
        await using (var meanwhile = database.CreateContext(tenant.Id))
        {
            var theirs = await meanwhile.Orders.SingleAsync(o => o.Id == orderId);
            theirs.Cancel(OrderStateMachineFactory.Create(BusinessType.Food, OrderType.Retail), "caja", "El cliente ya no lo quiso");
            await meanwhile.SaveChangesAsync();
        }

        // The trip changes the order too, so its version catches the cancellation instead of writing a folio on it.
        order.RegisterCourierShipment(CourierShipment.From(DeliveryCourier.Paquetenvia, "PQ1234", null), Now, "caja");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());
        await using var check = database.CreateContext(tenant.Id);
        Assert.Null((await check.OrderDeliveries.SingleAsync()).CourierFolio);
        Assert.Equal(OrderStatus.Cancelled, (await check.Orders.SingleAsync(o => o.Id == orderId)).Status);
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepTakingOrders_AndChargeAnOrderSentToAnAddressItsWholeTotal()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var orderId = await SeedDeliveryOrderAsync(database, tenant.Id);

        // The version deployed before (Fase 22B) does not know the table: it charges the order the total it was saved
        // with, delivery included, and keeps placing orders of its own.
        await using (var context = CreateVersionBeforeOrderDeliveries(database, tenant.Id))
        {
            var order = await context.Orders.Include(o => o.Payments).SingleAsync(o => o.Id == orderId);
            order.RegisterPayment(order.Total, OrderPaymentMethod.Cash, Now, null, null, null, null);
            context.Orders.Add(NewOrder(tenant.Id));
            await context.SaveChangesAsync();
        }

        await using var check = database.CreateContext(tenant.Id);
        var charged = await check.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal((135m, 135m, 35m), (charged.Total, charged.PaidAmount, charged.Delivery!.Fee));
        Assert.Equal(2, await check.Orders.CountAsync());
        Assert.Single(await check.OrderDeliveries.ToListAsync());
    }

    [SqlServerFact]
    public async Task Checks_ShouldRefuse_ARowTheDomainNeverWrites()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var orderId = await SeedOrderAsync(database, tenant.Id);
        (string PostalCode, string AddressLine, decimal Fee, decimal Rate, int? Courier, string? Folio, string? Link, bool Registered)[] rows =
        [
            ("7200", "Av. Juárez 123", 35m, 16m, null, null, null, false),
            ("7200a", "Av. Juárez 123", 35m, 16m, null, null, null, false),
            ("72000", "", 35m, 16m, null, null, null, false),
            ("72000", "Av. Juárez 123", -1m, 16m, null, null, null, false),
            ("72000", "Av. Juárez 123", 10_000.01m, 16m, null, null, null, false),
            ("72000", "Av. Juárez 123", 35m, 100.01m, null, null, null, false),
            ("72000", "Av. Juárez 123", 35m, 16m, 1, null, null, true),
            ("72000", "Av. Juárez 123", 35m, 16m, 1, "PQ1", null, false),
            ("72000", "Av. Juárez 123", 35m, 16m, 2, "PQ1", null, true),
            ("72000", "Av. Juárez 123", 35m, 16m, null, null, "https://paquetenvia.mx", false),
            ("72000", "Av. Juárez 123", 35m, 16m, 1, "PQ1", "http://paquetenvia.mx", true),
        ];
        await using var context = database.CreateContext();

        foreach (var row in rows)
        {
            DateTimeOffset? registeredAt = row.Registered ? Now : null;
            var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [OrderDeliveries] ([Id], [TenantId], [OrderId], [PostalCode], [AddressLine], [ZoneName], [Fee], [FeeTaxRate],
                    [Courier], [CourierFolio], [TrackingUrl], [ShipmentRegisteredAt])
                VALUES ({Guid.NewGuid()}, {tenant.Id}, {orderId}, {row.PostalCode}, {row.AddressLine}, N'Centro', {row.Fee}, {row.Rate},
                    {row.Courier}, {row.Folio}, {row.Link}, {registeredAt})
                """));
            Assert.True(error.Number == 547, $"{row}: error {error.Number}");
        }
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_WhileDeliveriesAreSaved_AndThenDropOnlyTheTable()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenant, _) = await SeedAsync(database);
        // The database stops at this migration: the app of this version (Fase 22C) writes and reads it.
        await using (var context = CreateVersionOfOrderDeliveries(database, tenant.Id))
        {
            var order = NewOrder(tenant.Id);
            var zone = new DeliveryZone(tenant.Id, new DeliveryZoneFields("Centro", 35m, 0m, null, ["72000"]));
            order.ArrangeDelivery(DeliveryAddress.From("72000", "Av. Juárez 123, Col. Centro", "Portón negro"), zone, VatRate.General);
            context.Orders.Add(order);
            await context.SaveChangesAsync();
        }

        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeOrderDeliveries));
        Assert.Equal(50013, error.Number);
        Assert.Contains("orders sent to an address", error.Message);
        await using (var context = CreateVersionOfOrderDeliveries(database, tenant.Id))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Single(await context.OrderDeliveries.ToListAsync());
            // Deleted on purpose, the rollback goes on.
            await context.Database.ExecuteSqlRawAsync("DELETE FROM [OrderDeliveries]");
        }

        await MigrateAsync(database, BeforeOrderDeliveries);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeOrderDeliveries, await LatestMigrationAsync(context));
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'OrderDeliveries'
                """).SingleAsync());
            Assert.Equal(1, await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [Orders]").SingleAsync());
        }
        // Rolling back again does nothing, and forward again the table is back, empty.
        await MigrateAsync(database, BeforeOrderDeliveries);
        await MigrateAsync(database, Migration);
        await using var forward = CreateVersionOfOrderDeliveries(database, tenant.Id);
        Assert.Empty(await forward.OrderDeliveries.ToListAsync());
    }

    private static async Task<Guid> SeedOrderAsync(SqlServerTestDatabase database, Guid tenantId)
    {
        await using var context = database.CreateContext(tenantId);
        var order = NewOrder(tenantId);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    /// <summary>A WhatsApp order of $100 in products, sent to 72000 by a zone that charges $35.</summary>
    private static async Task<Guid> SeedDeliveryOrderAsync(SqlServerTestDatabase database, Guid tenantId)
    {
        await using var context = database.CreateContext(tenantId);
        var order = NewOrder(tenantId);
        var zone = new DeliveryZone(tenantId, new DeliveryZoneFields("Centro", 35m, 0m, null, ["72000"]));
        order.ArrangeDelivery(DeliveryAddress.From("72000", "Av. Juárez 123, Col. Centro", "Portón negro"), zone, VatRate.General);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private static Order NewOrder(Guid tenantId)
    {
        var order = new Order(
            tenantId, null, null, createdByBusiness: true, $"A-{Guid.NewGuid():N}"[..10], BusinessType.Food, OrderType.Retail,
            "Ana", customerPhone: "5512345678", customerNotes: null, tableNumber: null, pricesIncludeTax: true,
            channel: OrderChannel.WhatsApp);
        order.AddItem(Guid.NewGuid(), "Comida corrida", 1, 100m);
        return order;
    }

    private static async Task<(Tenant Tenant, Tenant Other)> SeedAsync(SqlServerTestDatabase database)
    {
        var tenant = NewTenant("Cocina de Lupita");
        var other = NewTenant("Fonda vecina");
        await using var seed = database.CreateContext();
        seed.AddRange(tenant, other, new TenantSettings(tenant.Id, TenantCapability.Food), new TenantSettings(other.Id, TenantCapability.Food));
        await seed.SaveChangesAsync();
        return (tenant, other);
    }

    private static Tenant NewTenant(string name)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        return new Tenant(name, slug, BusinessType.Food, $"{slug}@demo.com");
    }

    private static VersionBeforeOrderDeliveriesContext CreateVersionBeforeOrderDeliveries(SqlServerTestDatabase database, Guid tenantId)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new VersionBeforeOrderDeliveriesContext(options, tenant, mediator));

    /// <summary>The app as deployed before Fase 22C: orders without deliveries.</summary>
    private sealed class VersionBeforeOrderDeliveriesContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentTenantService tenantService,
        IMediator mediator) : ApplicationDbContext(options, tenantService, mediator)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<OrderDelivery>();
        }
    }

    private static VersionOfOrderDeliveriesContext CreateVersionOfOrderDeliveries(SqlServerTestDatabase database, Guid tenantId)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new VersionOfOrderDeliveriesContext(options, tenant, mediator));

    /// <summary>The app of Fase 22C: deliveries without what went out with the courier (Fase 22D), and no settlements.</summary>
    private sealed class VersionOfOrderDeliveriesContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentTenantService tenantService,
        IMediator mediator) : ApplicationDbContext(options, tenantService, mediator)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Ignore<CourierSettlementAdjustment>();
            modelBuilder.Ignore<CourierSettlementOrder>();
            modelBuilder.Ignore<CourierSettlement>();
            modelBuilder.Entity<OrderDelivery>().Ignore(d => d.PaysWith);
            modelBuilder.Entity<OrderDelivery>().Ignore(d => d.DispatchedAt);
            modelBuilder.Entity<OrderDelivery>().Ignore(d => d.DispatchedBy);
            modelBuilder.Entity<OrderDelivery>().Ignore(d => d.CashToCollect);
        }
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
}
