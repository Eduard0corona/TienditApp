using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Finance.Entities;
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
/// An order that goes out with the courier and the courier's settlements (Fase 22D): what went out saved with the order and
/// caught by its version, a settlement kept in its business with its orders and adjustments, an order settled once, the
/// drawer kept under its settlements, the checks keeping every row one the domain could write, the version deployed before
/// still taking deliveries, and rolling back stopped while anything went out and otherwise dropping only what it added.
/// </summary>
public class CourierSettlementsSqlServerTests
{
    private const string BeforeCourierSettlements = "20261013042014_AddOrderDeliveries";
    private const string Migration = "20261013085926_AddCourierSettlements";
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 19, 0, 0, TimeSpan.Zero);
    private static readonly CourierShipment Trip = CourierShipment.From(DeliveryCourier.Paquetenvia, "PQ48213", "https://paquetenvia.mx/r/PQ48213");

    [SqlServerFact]
    public async Task Dispatch_ShouldBeSaved_WithTheOrder()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var orderId = await SeedDeliveryOrderAsync(database, tenant.Id, paysWith: 200m);

        await using (var context = database.CreateContext(tenant.Id))
        {
            var order = await context.Orders.SingleAsync(o => o.Id == orderId);
            order.Dispatch(Trip, collectsCash: true, paysWith: 200m, Now, "caja");
            await context.SaveChangesAsync();
        }

        await using var check = database.CreateContext(tenant.Id);
        var delivery = (await check.Orders.SingleAsync(o => o.Id == orderId)).Delivery!;
        Assert.Equal((Now, "caja", 135m, 200m, "PQ48213"),
            (delivery.DispatchedAt, delivery.DispatchedBy, delivery.CashToCollect, delivery.PaysWith, delivery.CourierFolio));
    }

    [SqlServerFact]
    public async Task Dispatch_ShouldConflict_WithAPaymentSavedMeanwhile()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var orderId = await SeedDeliveryOrderAsync(database, tenant.Id);
        await using var context = database.CreateContext(tenant.Id);
        var order = await context.Orders.SingleAsync(o => o.Id == orderId);

        // Someone charges the order after this request read it, and before it sends the order out to be collected.
        await using (var meanwhile = database.CreateContext(tenant.Id))
        {
            var theirs = await meanwhile.Orders.Include(o => o.Payments).SingleAsync(o => o.Id == orderId);
            theirs.RegisterPayment(theirs.Total, OrderPaymentMethod.Transfer, Now, null, null, null, null);
            await meanwhile.SaveChangesAsync();
        }

        // What the courier collects is the balance this request read, so the order's version refuses it.
        order.Dispatch(Trip, collectsCash: true, paysWith: null, Now, "caja");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => context.SaveChangesAsync());
        await using var check = database.CreateContext(tenant.Id);
        Assert.Null((await check.OrderDeliveries.SingleAsync()).CashToCollect);
    }

    [SqlServerFact]
    public async Task Settlement_ShouldBeSaved_InItsBusiness_WithItsOrdersAndAdjustmentsInOrder()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, other) = await SeedAsync(database);
        var first = await SeedDeliveryOrderAsync(database, tenant.Id);
        var second = await SeedDeliveryOrderAsync(database, tenant.Id);
        var drawer = await SeedDrawerAsync(database, tenant.Id);

        Guid settlementId;
        await using (var context = database.CreateContext(tenant.Id))
        {
            var settlement = Settlement(tenant.Id, drawer, 220m, [(first, 135m), (second, 135m)],
                [new(CourierSettlementAdjustmentKind.TripFee, null, -40m), new(CourierSettlementAdjustmentKind.Other, "Redondeo", -10m)]);
            context.CourierSettlements.Add(settlement);
            await context.SaveChangesAsync();
            settlementId = settlement.Id;
        }

        await using (var check = database.CreateContext(tenant.Id))
        {
            var saved = await check.CourierSettlements.Include(s => s.Orders).Include(s => s.Adjustments).SingleAsync();
            Assert.Equal((settlementId, 220m, 270m, -50m, drawer, OrderPaymentMethod.Cash),
                (saved.Id, saved.HandedAmount, saved.OrdersTotal, saved.AdjustmentsTotal, saved.CashSessionId, saved.Method));
            Assert.Equal(new[] { first, second }.Order(), saved.Orders.Select(o => o.OrderId).Order());
            Assert.All(saved.Orders, o => Assert.Equal(135m, o.Amount));
            Assert.Equal([(1, -40m), (2, -10m)], saved.Adjustments.Select(a => (a.Position, a.Amount)));
        }

        await using var neighbor = database.CreateContext(other.Id);
        Assert.Empty(await neighbor.CourierSettlements.ToListAsync());
        Assert.Empty(await neighbor.CourierSettlementOrders.ToListAsync());
        Assert.Empty(await neighbor.CourierSettlementAdjustments.ToListAsync());
    }

    [SqlServerFact]
    public async Task Order_ShouldBeSettledOnce_ByTheIndex_EvenWhenTheAppMissedTheFirstSettlement()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var orderId = await SeedDeliveryOrderAsync(database, tenant.Id);
        var drawer = await SeedDrawerAsync(database, tenant.Id);

        await using (var context = database.CreateContext(tenant.Id))
        {
            context.CourierSettlements.Add(Settlement(tenant.Id, drawer, 135m, [(orderId, 135m)]));
            await context.SaveChangesAsync();
        }

        await using var again = database.CreateContext(tenant.Id);
        again.CourierSettlements.Add(Settlement(tenant.Id, drawer, 135m, [(orderId, 135m)]));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => again.SaveChangesAsync());
        Assert.Equal(2601, Assert.IsType<SqlException>(error.InnerException).Number);
    }

    [SqlServerFact]
    public async Task Drawer_ShouldStayUnderItsSettlement_WhileTheSettlementTakesItsRowsAway()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var orderId = await SeedDeliveryOrderAsync(database, tenant.Id);
        var drawer = await SeedDrawerAsync(database, tenant.Id);
        await using var context = database.CreateContext(tenant.Id);
        context.CourierSettlements.Add(Settlement(tenant.Id, drawer, 125m, [(orderId, 135m)],
            [new(CourierSettlementAdjustmentKind.TripFee, null, -10m)]));
        await context.SaveChangesAsync();

        var drawerGone = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [CashSessions] WHERE [Id] = {drawer}"));
        var orderGone = await Assert.ThrowsAsync<SqlException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Orders] WHERE [Id] = {orderId}"));
        Assert.Equal((547, 547), (drawerGone.Number, orderGone.Number));

        await context.Database.ExecuteSqlRawAsync("DELETE FROM [CourierSettlements]");
        Assert.Equal((0, 0), (
            await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [CourierSettlementOrders]").SingleAsync(),
            await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [CourierSettlementAdjustments]").SingleAsync()));
    }

    [SqlServerFact]
    public async Task Checks_ShouldRefuse_ARowTheDomainNeverWrites()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var orderId = await SeedDeliveryOrderAsync(database, tenant.Id);
        var drawer = await SeedDrawerAsync(database, tenant.Id);
        var settlementId = Guid.NewGuid();
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [CourierSettlements] ([Id], [TenantId], [Courier], [CashSessionId], [Method], [HandedAmount], [OrdersTotal],
                [AdjustmentsTotal], [SettledAt], [CreatedAt])
            VALUES ({settlementId}, {tenant.Id}, 1, {drawer}, 0, 0, 0, 0, {Now}, {Now})
            """);

        // What went out, a bill and what the courier collects, on the delivery.
        foreach (var (column, value) in new (string, string)[]
        {
            ("[PaysWith] = 0", "a bill of $0"),
            ("[PaysWith] = 100000.01", "a bill too big"),
            ("[DispatchedAt] = SYSDATETIMEOFFSET()", "out with no courier"),
            ("[CashToCollect] = 135", "collected without going out"),
        })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
                $"UPDATE [OrderDeliveries] SET {column} WHERE [OrderId] = '{orderId}'"));
            Assert.True(error.Number == 547, $"{value}: error {error.Number}");
        }

        // A settlement: a known courier, cash or a transfer, nothing handed below zero and adding up to the cent.
        foreach (var (courier, method, handed, orders, adjustments) in new (int, int, decimal, decimal, decimal)[]
        {
            (2, 0, 0m, 0m, 0m), (1, 1, 0m, 0m, 0m), (1, 5, 0m, 0m, 0m), (1, 0, -10m, 0m, -10m), (1, 0, 480m, 500m, 0m),
        })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [CourierSettlements] ([Id], [TenantId], [Courier], [CashSessionId], [Method], [HandedAmount], [OrdersTotal],
                    [AdjustmentsTotal], [SettledAt], [CreatedAt])
                VALUES ({Guid.NewGuid()}, {tenant.Id}, {courier}, {drawer}, {method}, {handed}, {orders}, {adjustments}, {Now}, {Now})
                """));
            Assert.True(error.Number == 547, $"({courier}, {method}, {handed}, {orders}, {adjustments}): error {error.Number}");
        }

        // An order paid for something; an adjustment of a known kind, deducted unless «Otro», which says what it is.
        var line = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [CourierSettlementOrders] ([Id], [TenantId], [SettlementId], [OrderId], [Amount])
            VALUES ({Guid.NewGuid()}, {tenant.Id}, {settlementId}, {orderId}, {0m})
            """));
        Assert.Equal(547, line.Number);
        foreach (var (kind, description, amount) in new (int, string?, decimal)[]
        {
            (4, null, -10m), (1, null, 10m), (3, null, 0m), (99, null, 10m), (99, "Redondeo", 0m),
        })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [CourierSettlementAdjustments] ([Id], [TenantId], [SettlementId], [Position], [Kind], [Description], [Amount])
                VALUES ({Guid.NewGuid()}, {tenant.Id}, {settlementId}, 1, {kind}, {description}, {amount})
                """));
            Assert.True(error.Number == 547, $"({kind}, {description}, {amount}): error {error.Number}");
        }
    }

    [SqlServerFact]
    public async Task PreviousVersion_ShouldKeepTakingDeliveries_AndWritingTripsDown()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var (tenant, _) = await SeedAsync(database);
        var dispatched = await SeedDeliveryOrderAsync(database, tenant.Id);
        await using (var context = database.CreateContext(tenant.Id))
        {
            (await context.Orders.SingleAsync(o => o.Id == dispatched)).Dispatch(Trip, collectsCash: true, paysWith: null, Now, "caja");
            await context.SaveChangesAsync();
        }

        // The version deployed before (Fase 22C) does not know what went out: it places orders to the address, writes a trip
        // down and charges an order that went out, leaving what this version wrote as it was.
        await using (var context = CreateVersionBeforeCourierSettlements(database, tenant.Id))
        {
            var order = NewOrder(tenant.Id);
            order.ArrangeDelivery(Address, Zone(tenant.Id), VatRate.General);
            context.Orders.Add(order);
            var outThere = await context.Orders.Include(o => o.Payments).SingleAsync(o => o.Id == dispatched);
            outThere.RegisterCourierShipment(CourierShipment.From(DeliveryCourier.Paquetenvia, "PQ9", null), Now.AddMinutes(5), "otro");
            outThere.RegisterPayment(outThere.Total, OrderPaymentMethod.Cash, Now, null, null, null, null);
            await context.SaveChangesAsync();
        }

        await using var check = database.CreateContext(tenant.Id);
        var deliveries = await check.OrderDeliveries.ToListAsync();
        Assert.Equal(2, deliveries.Count);
        var kept = deliveries.Single(d => d.OrderId == dispatched);
        Assert.Equal(("PQ9", Now, 135m), (kept.CourierFolio, kept.DispatchedAt, kept.CashToCollect));
        Assert.Single(deliveries, d => d.DispatchedAt is null && d.PaysWith is null && d.CashToCollect is null);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_WhileAnythingWentOut_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var (tenant, _) = await SeedAsync(database);
        var orderId = await SeedDeliveryOrderAsync(database, tenant.Id, paysWith: 200m);
        var drawer = await SeedDrawerAsync(database, tenant.Id);
        await using (var context = database.CreateContext(tenant.Id))
        {
            context.CourierSettlements.Add(Settlement(tenant.Id, drawer, 135m, [(orderId, 135m)]));
            await context.SaveChangesAsync();
        }

        // A settlement stops it; so does a bill alone, once the settlement is deleted on purpose.
        foreach (var cleanUp in new[] { "DELETE FROM [CourierSettlements]", "UPDATE [OrderDeliveries] SET [PaysWith] = NULL" })
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeCourierSettlements));
            Assert.Equal(50014, error.Number);
            Assert.Contains("sent out with the courier or courier settlements", error.Message);
            await using var context = database.CreateContext(tenant.Id);
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            await context.Database.ExecuteSqlRawAsync(cleanUp);
        }

        await MigrateAsync(database, BeforeCourierSettlements);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeCourierSettlements, await LatestMigrationAsync(context));
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] LIKE N'CourierSettlement%'
                """).SingleAsync());
            Assert.Equal(0, await context.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'dbo.OrderDeliveries') AND [name] IN (N'PaysWith', N'DispatchedAt', N'DispatchedBy', N'CashToCollect')
                """).SingleAsync());
            Assert.Equal((1, 1), (
                await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [OrderDeliveries]").SingleAsync(),
                await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [CashSessions]").SingleAsync()));
        }

        // Rolling back again does nothing, and forward again the tables are back, empty.
        await MigrateAsync(database, BeforeCourierSettlements);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(tenant.Id);
        Assert.Empty(await forward.CourierSettlements.ToListAsync());
        Assert.Null((await forward.OrderDeliveries.SingleAsync()).DispatchedAt);
    }

    private static readonly DeliveryAddress Address = DeliveryAddress.From("72000", "Av. Juárez 123, Col. Centro", null);

    private static CourierSettlement Settlement(
        Guid tenantId,
        Guid drawer,
        decimal handed,
        (Guid OrderId, decimal Amount)[] orders,
        CourierSettlementAdjustmentInput[]? adjustments = null)
        => new(tenantId, DeliveryCourier.Paquetenvia, drawer, OrderPaymentMethod.Cash, handed,
            orders.Select(o => new CourierSettlementOrderInput(o.OrderId, o.Amount)), adjustments ?? [], null, null, Now, "caja");

    /// <summary>A WhatsApp order of $100 in products, sent to 72000 by a zone that charges $35, confirmed.</summary>
    private static async Task<Guid> SeedDeliveryOrderAsync(SqlServerTestDatabase database, Guid tenantId, decimal? paysWith = null)
    {
        // Also on a database migrated to an earlier migration, without the tracking link of Fase 24C.
        await using var context = BeforeOrderTracking.CreateContext(database, tenantId);
        var order = NewOrder(tenantId);
        order.ArrangeDelivery(Address, Zone(tenantId), VatRate.General, paysWith);
        order.TransitionTo(OrderStatus.Confirmed, OrderStateMachineFactory.Create(BusinessType.Food, OrderType.Retail), "caja");
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    private static async Task<Guid> SeedDrawerAsync(SqlServerTestDatabase database, Guid tenantId)
    {
        await using var context = database.CreateContext(tenantId);
        var drawer = new CashSession(tenantId, Now.AddHours(-2), "caja", 500m);
        context.CashSessions.Add(drawer);
        await context.SaveChangesAsync();
        return drawer.Id;
    }

    private static DeliveryZone Zone(Guid tenantId) => new(tenantId, new DeliveryZoneFields("Centro", 35m, 0m, null, ["72000"]));

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
        // Without what Fase 22E added: some tests seed a database migrated to an earlier migration.
        await using var seed = BeforeHomeVisits.CreateContext(database);
        seed.AddRange(tenant, other, new TenantSettings(tenant.Id, TenantCapability.Food), new TenantSettings(other.Id, TenantCapability.Food));
        await seed.SaveChangesAsync();
        return (tenant, other);
    }

    private static Tenant NewTenant(string name)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        return new Tenant(name, slug, BusinessType.Food, $"{slug}@demo.com");
    }

    private static VersionBeforeCourierSettlementsContext CreateVersionBeforeCourierSettlements(SqlServerTestDatabase database, Guid tenantId)
        => database.CreateContext(tenantId, (options, tenant, mediator) => new VersionBeforeCourierSettlementsContext(options, tenant, mediator));

    /// <summary>The app as deployed before Fase 22D: deliveries without what went out, and no settlements.</summary>
    private sealed class VersionBeforeCourierSettlementsContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentTenantService tenantService,
        IMediator mediator) : ApplicationDbContext(options, tenantService, mediator)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            BeforeHomeVisits.Ignore(modelBuilder);
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
