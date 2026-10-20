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
/// AddCustomerWriteOffs (Fase 26B, ADR 0034): what the business forgives of an order, to the centavo, with its record, and
/// the term of its fiado; what the deployed version stores meanwhile forgives nothing and has no term; the checks refuse
/// what the domain never writes; and rolling back stops with error 50027 once a business forgave something or set a term,
/// and otherwise drops only the table, the columns and their checks.
/// </summary>
public class CustomerWriteOffsMigrationSqlServerTests
{
    // The migration right before AddCustomerWriteOffs, where its rollback.sql takes the database.
    private const string BeforeWriteOffs = "20261019202512_AddCustomerCredit";
    private const string Migration = "20261020003342_AddCustomerWriteOffs";
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Columns_ShouldKeepWhatWasForgivenToTheCentavo_AndTheTerm_WhileThePreviousVersionStoresNeither()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeWriteOffs);
        var tenant = await SeedTenantAsync(database);
        await using (var before = BeforeCustomerWriteOffs.CreateContext(database, tenant.Id))
        {
            before.Orders.Add(SoldOnCredit(tenant.Id));
            await before.SaveChangesAsync();
        }

        await MigrateAsync(database, Migration);

        // The version deployed before does not know them: what it stores forgives nothing, and its business has no term.
        await using (var previous = BeforeCustomerWriteOffs.CreateContext(database, tenant.Id))
        {
            previous.Orders.Add(SoldOnCredit(tenant.Id));
            (await previous.TenantSettings.SingleAsync()).UpdateSalesPolicy(true, 16m, true);
            await previous.SaveChangesAsync();
        }
        Guid forgiven;
        await using (var context = BeforeCustomerCreditReminders.CreateContext(database, tenant.Id))
        {
            var orders = await context.Orders.ToListAsync();
            Assert.Equal(2, orders.Count);
            Assert.All(orders, order => Assert.Equal((0m, order.Total - order.PaidAmount), (order.WrittenOffAmount, order.Balance)));
            var settings = await context.TenantSettings.SingleAsync();
            Assert.Null(settings.CreditTermDays);

            forgiven = orders[0].Id;
            orders[0].WriteOff(25.50m, OrderWriteOffReason.Other, "Pagó con trabajo", Now, "dueno@tienda.mx");
            settings.SetCreditTermDays(15);
            await context.SaveChangesAsync();
        }

        await using (var check = BeforeCustomerCreditReminders.CreateContext(database, tenant.Id))
        {
            var order = await check.Orders.Include(o => o.WriteOffs).SingleAsync(o => o.Id == forgiven);
            var writeOff = Assert.Single(order.WriteOffs);
            Assert.Equal((25.50m, order.Total - 25.50m), (order.WrittenOffAmount, order.Balance));
            Assert.Equal(
                (tenant.Id, 25.50m, OrderWriteOffReason.Other, "Pagó con trabajo", Now, "dueno@tienda.mx", (DateTimeOffset?)null),
                (writeOff.TenantId, writeOff.Amount, writeOff.Reason, writeOff.Notes, writeOff.WrittenOffAt, writeOff.WrittenOffBy, writeOff.ReversedAt));
            Assert.Equal(15, (await check.TenantSettings.SingleAsync()).CreditTermDays);

            // Undone by mistake: the record stays, with when and who undid it, and the order owes it again.
            order.ReverseWriteOff(writeOff.Id, Now.AddHours(1), "admin@tienda.mx");
            await check.SaveChangesAsync();
        }

        await using var undone = BeforeCustomerCreditReminders.CreateContext(database, tenant.Id);
        var reversedOrder = await undone.Orders.Include(o => o.WriteOffs).SingleAsync(o => o.Id == forgiven);
        var reversed = Assert.Single(reversedOrder.WriteOffs);
        Assert.Equal((0m, reversedOrder.Total), (reversedOrder.WrittenOffAmount, reversedOrder.Balance));
        Assert.Equal((25.50m, Now.AddHours(1), "admin@tienda.mx"), (reversed.Amount, reversed.ReversedAt!.Value, reversed.ReversedBy));
    }

    [SqlServerFact]
    public async Task Checks_ShouldRefuse_ANegativeWriteOff_AWriteOffOfZero_AndATermOutOfRange()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        Guid orderId;
        await using (var context = BeforeCustomerCreditReminders.CreateContext(database, tenant.Id))
        {
            var order = SoldOnCredit(tenant.Id);
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            orderId = order.Id;
        }

        await using var check = BeforeCustomerCreditReminders.CreateContext(database);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlAsync(
            $"UPDATE [Orders] SET [WrittenOffAmount] = -1 WHERE [Id] = {orderId}"))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlAsync(
            $"UPDATE [TenantSettings] SET [CreditTermDays] = 121"))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlAsync(
            $"UPDATE [TenantSettings] SET [CreditTermDays] = 0"))).Number);
        Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlAsync($"""
            INSERT INTO [OrderWriteOffs] ([Id], [TenantId], [OrderId], [Amount], [Reason], [WrittenOffAt], [WrittenOffBy], [CreatedAt])
            VALUES (NEWID(), {tenant.Id}, {orderId}, 0, 1, SYSDATETIMEOFFSET(), N'prueba', SYSDATETIMEOFFSET())
            """))).Number);
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceABusinessForgaveOrSetATerm_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        await using (var context = BeforeCustomerCreditReminders.CreateContext(database, tenant.Id))
        {
            var order = SoldOnCredit(tenant.Id);
            order.WriteOff(10m, OrderWriteOffReason.WontBeCollected, null, Now, "dueno");
            context.Orders.Add(order);
            await context.SaveChangesAsync();
        }

        // A write-off alone stops it.
        var forgiven = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeWriteOffs));
        Assert.Equal(50027, forgiven.Number);
        Assert.Contains("wrote off customer debts", forgiven.Message);

        // And a term alone too.
        await using (var context = BeforeCustomerCreditReminders.CreateContext(database))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            await context.Database.ExecuteSqlRawAsync("DELETE FROM [OrderWriteOffs]; UPDATE [Orders] SET [WrittenOffAmount] = 0; UPDATE [TenantSettings] SET [CreditTermDays] = 30");
        }
        var term = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeWriteOffs));
        Assert.Equal(50027, term.Number);
        await using (var context = BeforeCustomerCreditReminders.CreateContext(database))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(30, await CountAsync(context, $"SELECT [CreditTermDays] AS [Value] FROM [TenantSettings]"));
            await context.Database.ExecuteSqlRawAsync("UPDATE [TenantSettings] SET [CreditTermDays] = NULL");
        }

        await MigrateAsync(database, BeforeWriteOffs);
        await using (var context = BeforeCustomerCreditReminders.CreateContext(database))
        {
            Assert.Equal(BeforeWriteOffs, await LatestMigrationAsync(context));
            Assert.Equal((0, 0, 0, 0, 1), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.Orders') AND [name] = N'WrittenOffAmount'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.TenantSettings') AND [name] = N'CreditTermDays'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'OrderWriteOffs'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE [name] IN (N'CK_Orders_WrittenOffAmount', N'CK_TenantSettings_CreditTermDays', N'CK_OrderWriteOffs_Amount')"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Orders] WHERE [Total] > [PaidAmount] AND [SoldOnCredit] = 1")));
        }

        // Rolling back again does nothing, and forward again the sale is there, owed in full.
        await MigrateAsync(database, BeforeWriteOffs);
        await MigrateAsync(database, Migration);
        await using var forward = BeforeCustomerCreditReminders.CreateContext(database, tenant.Id);
        var sale = await forward.Orders.SingleAsync();
        Assert.Equal((0m, sale.Total), (sale.WrittenOffAmount, sale.Balance));
        Assert.Null((await forward.TenantSettings.SingleAsync()).CreditTermDays);
    }

    /// <summary>A counter sale sold on credit and closed, as Vender stores it.</summary>
    private static Order SoldOnCredit(Guid tenantId)
    {
        var stateMachine = new RetailOrderStateMachine();
        var order = new Order(
            tenantId, null, null, createdByBusiness: true, $"A-{Guid.NewGuid():N}"[..10], BusinessType.Retail,
            OrderType.Retail, "Valeria", customerPhone: "+525522107765", customerNotes: null, tableNumber: null);
        order.AddItem(Guid.NewGuid(), "Jabón", 2, 35m);
        order.TransitionTo(OrderStatus.Confirmed, stateMachine, "cajero");
        order.SellOnCredit();
        order.CloseIfPaidCounterSale(stateMachine, waitsForKitchen: false, "cajero");
        return order;
    }

    private static async Task<Tenant> SeedTenantAsync(SqlServerTestDatabase database)
    {
        var slug = Guid.NewGuid().ToString("N")[..10];
        var tenant = new Tenant($"Tienda {slug}", slug, BusinessType.Retail, $"{slug}@demo.com");
        await using var seed = BeforeCustomerWriteOffs.CreateContext(database);
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
