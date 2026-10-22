using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Finance.Enums;
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
/// AddCustomerBalance (Fase 26D, ADR 0036): the movements of the balance in a customer's favor, each with the sign of its kind
/// and tied to what moves it, a payment or a refund moving it once, per business; the checks refuse what the domain never
/// writes; and rolling back stops with error 50029 once a customer left a balance or paid, got a refund or a drawer moved
/// money with it, and otherwise drops only the table. Its database stops before the notice of Fase 26E, whose columns the
/// settings of this version do not have.
/// </summary>
public class CustomerBalanceMigrationSqlServerTests
{
    // The migration right before AddCustomerBalance, where its rollback.sql takes the database.
    private const string BeforeBalance = "20261020055548_AddCustomerCreditReminders";
    private const string Migration = "20261020145628_AddCustomerBalance";
    private const string Phone = "+525522107765";
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [SqlServerFact]
    public async Task Table_ShouldKeepTheMovementsOfEachAccount_WithTheSignOfTheirKind_TiedToWhatTheyMove()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        var other = await SeedTenantAsync(database);
        Guid paymentId;
        await using (var context = BeforeCreditDueSoon.CreateContext(database, tenant.Id))
        {
            var drawer = new CashSession(tenant.Id, Now, "cajero", 500m);
            var order = CounterSale(tenant.Id);
            var payment = order.RegisterPayment(30m, OrderPaymentMethod.CustomerBalance, Now, null, null, null, null);
            order.RegisterPayment(40m, OrderPaymentMethod.Cash, Now, null, null, null, null);
            var refund = order.RegisterRefund(10m, OrderPaymentMethod.CustomerBalance, "No le gustó", Now, "dueno", null, null, false, []);
            paymentId = payment.Id;
            context.AddRange(drawer, order,
                CustomerBalanceEntry.Deposit(tenant.Id, Phone, null, 100m, OrderPaymentMethod.Cash, drawer.Id, null, "Anticipo", Now, "cajero"),
                CustomerBalanceEntry.Applied(tenant.Id, Phone, null, 30m, order.Id, payment.Id, Now, "cajero"),
                CustomerBalanceEntry.Refunded(tenant.Id, Phone, null, 10m, order.Id, refund.Id, Now, "dueno"),
                CustomerBalanceEntry.Returned(tenant.Id, Phone, null, 20.50m, OrderPaymentMethod.Card, drawer.Id, "Voucher 77", null, Now, "dueno"));
            await context.SaveChangesAsync();
        }

        await using (var check = BeforeCreditDueSoon.CreateContext(database, tenant.Id))
        {
            var entries = await check.CustomerBalanceEntries.Where(e => e.Phone == Phone).OrderBy(e => e.Kind).ToListAsync();
            Assert.Equal([100m, 10m, -30m, -20.50m], entries.Select(e => e.Amount));
            Assert.Equal(59.50m, entries.Sum(e => e.Amount));
            Assert.Equal(((OrderPaymentMethod?)OrderPaymentMethod.Card, "Voucher 77"), (entries[3].Method, entries[3].Reference));
        }
        await using (var elsewhere = BeforeCreditDueSoon.CreateContext(database, other.Id))
            Assert.Empty(await elsewhere.CustomerBalanceEntries.ToListAsync());

        // A payment with the balance takes from it once.
        await using var raw = BeforeCreditDueSoon.CreateContext(database);
        var twice = await Assert.ThrowsAsync<SqlException>(() => raw.Database.ExecuteSqlAsync($"""
            INSERT INTO [CustomerBalanceEntries] ([Id], [TenantId], [Phone], [Kind], [Amount], [OrderId], [OrderPaymentId], [OccurredAt], [RecordedBy], [CreatedAt])
            SELECT {Guid.NewGuid()}, [TenantId], [Phone], 2, -1, [OrderId], [OrderPaymentId], [OccurredAt], N'cajero', SYSDATETIMEOFFSET()
            FROM [CustomerBalanceEntries] WHERE [OrderPaymentId] = {paymentId}
            """));
        Assert.Equal(2601, twice.Number);
        Assert.Contains("IX_CustomerBalanceEntries_OrderPaymentId", twice.Message);
    }

    [SqlServerFact]
    public async Task Checks_ShouldRefuse_WhatTheDomainNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        await SeedEveryKindAsync(database, tenant.Id);

        (string Sql, string What)[] refusals =
        [
            ("UPDATE [CustomerBalanceEntries] SET [Amount] = -[Amount] WHERE [Kind] = 0", "a deposit that takes"),
            ("UPDATE [CustomerBalanceEntries] SET [Amount] = -[Amount] WHERE [Kind] = 1", "a refund that takes"),
            ("UPDATE [CustomerBalanceEntries] SET [Amount] = -[Amount] WHERE [Kind] = 2", "a payment that adds"),
            ("UPDATE [CustomerBalanceEntries] SET [Amount] = -[Amount] WHERE [Kind] = 3", "a return that adds"),
            ("UPDATE [CustomerBalanceEntries] SET [Amount] = 0 WHERE [Kind] = 0", "a movement of nothing"),
            ("UPDATE [CustomerBalanceEntries] SET [Kind] = 4 WHERE [Kind] = 0", "a kind that is not one"),
            ("UPDATE [CustomerBalanceEntries] SET [Method] = NULL WHERE [Kind] = 0", "a deposit without its method"),
            ("UPDATE [CustomerBalanceEntries] SET [CashSessionId] = NULL WHERE [Kind] = 3", "a return without its drawer"),
            ("UPDATE [CustomerBalanceEntries] SET [OrderRefundId] = NULL WHERE [Kind] = 1", "a refund without the refund"),
            ("UPDATE [CustomerBalanceEntries] SET [OrderPaymentId] = NULL WHERE [Kind] = 2", "a payment without the payment"),
            ("UPDATE [CustomerBalanceEntries] SET [OrderId] = NULL WHERE [Kind] = 2", "a payment without its order"),
        ];
        await using var context = BeforeCreditDueSoon.CreateContext(database);
        foreach (var (sql, what) in refusals)
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(sql));
            Assert.True(refused.Number == 547, $"{what}: {refused.Message}");
        }
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceTheBalanceWasUsed_AndThenDropOnlyTheTable()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var tenant = await SeedTenantAsync(database);
        await using (var context = BeforeCreditDueSoon.CreateContext(database, tenant.Id))
        {
            // What the previous version stores too: a sale paid in cash and partly refunded, and a drawer with a deposit.
            var drawer = new CashSession(tenant.Id, Now, "cajero", 500m);
            drawer.RegisterTransaction(CashTransactionType.Deposit, 50m, Now);
            var order = CounterSale(tenant.Id);
            order.RegisterPayment(70m, OrderPaymentMethod.Cash, Now, null, null, null, null);
            order.RegisterRefund(10m, OrderPaymentMethod.Cash, "Precio", Now, "dueno", null, null, false, []);
            context.AddRange(drawer, order,
                CustomerBalanceEntry.Deposit(tenant.Id, Phone, null, 100m, OrderPaymentMethod.Cash, drawer.Id, null, null, Now, "cajero"));
            await context.SaveChangesAsync();
        }

        // A movement of the balance alone stops it.
        var left = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeBalance));
        Assert.Equal(50029, left.Number);
        Assert.Contains("balance in their favor", left.Message);
        await ExecuteAsync(database, "DELETE FROM [CustomerBalanceEntries]");

        // And so does each trace of it outside the table: a payment or a refund with it, and what a drawer took in or gave back.
        (string Trace, string Undo)[] traces =
        [
            ("UPDATE [OrderPayments] SET [Method] = 7", "UPDATE [OrderPayments] SET [Method] = 0"),
            ("UPDATE [OrderRefunds] SET [Method] = 7", "UPDATE [OrderRefunds] SET [Method] = 0"),
            ("UPDATE [CashTransactions] SET [TransactionType] = 8", "UPDATE [CashTransactions] SET [TransactionType] = 4"),
            ("UPDATE [CashTransactions] SET [TransactionType] = 9, [Amount] = -[Amount]", "UPDATE [CashTransactions] SET [TransactionType] = 4, [Amount] = -[Amount]"),
            ("UPDATE [CashTransactions] SET [PaymentMethod] = 7", "UPDATE [CashTransactions] SET [PaymentMethod] = NULL"),
        ];
        foreach (var (trace, undo) in traces)
        {
            await ExecuteAsync(database, trace);
            var used = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeBalance));
            Assert.True(used.Number == 50029, $"{trace}: {used.Message}");
            await using (var context = BeforeCreditDueSoon.CreateContext(database))
                Assert.Equal(Migration, await LatestMigrationAsync(context));
            await ExecuteAsync(database, undo);
        }

        await MigrateAsync(database, BeforeBalance);
        await using (var context = BeforeCreditDueSoon.CreateContext(database))
        {
            Assert.Equal(BeforeBalance, await LatestMigrationAsync(context));
            Assert.Equal((0, 1, 1, 1), (
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] = N'CustomerBalanceEntries'"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [OrderPayments]"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [OrderRefunds]"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [CashTransactions]")));
        }

        // Rolling back again does nothing, and forward again the table is there, empty.
        await MigrateAsync(database, BeforeBalance);
        await MigrateAsync(database, Migration);
        await using var forward = BeforeCreditDueSoon.CreateContext(database, tenant.Id);
        Assert.Empty(await forward.CustomerBalanceEntries.ToListAsync());
        Assert.Equal(60m, (await forward.Orders.SingleAsync()).NetPaidAmount);
    }

    /// <summary>One movement of each kind, as the domain writes them.</summary>
    private static async Task SeedEveryKindAsync(SqlServerTestDatabase database, Guid tenantId)
    {
        await using var context = BeforeCreditDueSoon.CreateContext(database, tenantId);
        var drawer = new CashSession(tenantId, Now, "cajero", 0m);
        var order = CounterSale(tenantId);
        var payment = order.RegisterPayment(70m, OrderPaymentMethod.CustomerBalance, Now, null, null, null, null);
        var refund = order.RegisterRefund(5m, OrderPaymentMethod.CustomerBalance, "Precio", Now, "dueno", null, null, false, []);
        context.AddRange(drawer, order,
            CustomerBalanceEntry.Deposit(tenantId, Phone, null, 100m, OrderPaymentMethod.Cash, drawer.Id, null, null, Now, "cajero"),
            CustomerBalanceEntry.Refunded(tenantId, Phone, null, 5m, order.Id, refund.Id, Now, "dueno"),
            CustomerBalanceEntry.Applied(tenantId, Phone, null, 70m, order.Id, payment.Id, Now, "cajero"),
            CustomerBalanceEntry.Returned(tenantId, Phone, null, 35m, OrderPaymentMethod.Cash, drawer.Id, null, null, Now, "dueno"));
        await context.SaveChangesAsync();
    }

    private static Order CounterSale(Guid tenantId)
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
        await using var seed = BeforeCreditDueSoon.CreateContext(database);
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

    private static async Task ExecuteAsync(SqlServerTestDatabase database, string sql)
    {
        await using var context = BeforeCreditDueSoon.CreateContext(database);
        await context.Database.ExecuteSqlRawAsync(sql);
    }

    private static Task<string> LatestMigrationAsync(ApplicationDbContext context) =>
        context.Database.SqlQuery<string>($"""
            SELECT TOP (1) [MigrationId] AS [Value] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC
            """).SingleAsync();

    private static Task<int> CountAsync(ApplicationDbContext context, FormattableString sql) =>
        context.Database.SqlQuery<int>(sql).SingleAsync();
}
