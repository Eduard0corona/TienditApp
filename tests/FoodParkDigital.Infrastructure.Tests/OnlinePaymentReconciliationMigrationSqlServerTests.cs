using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Enums;
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
/// AddOnlinePaymentReconciliation (Fase 24K, ADR 0044): what goes back in Mercado Pago and the disputes reach the order; what
/// the deployed version stores meanwhile has no dispute; each refund of Mercado Pago is recorded once and a lost chargeback
/// once per payment; applying stops with error 50036 before two refunds with one id; and rolling back stops with error 50035
/// once anything came from Mercado Pago, and otherwise drops only what it added.
/// </summary>
public class OnlinePaymentReconciliationMigrationSqlServerTests
{
    // The migration right before AddOnlinePaymentReconciliation, where its rollback.sql takes the database.
    private const string BeforeReconciliation = "20261022153116_AddCreditDueSoon";
    private const string Migration = "20261022213253_AddOnlinePaymentReconciliation";
    private const long ProviderUserId = 1234567890;

    [SqlServerFact]
    public async Task Columns_ShouldLeaveEveryCheckoutWithoutADispute_WhileThePreviousVersionStoresThem()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeReconciliation);
        var seed = await SeedAsync(database);

        await MigrateAsync(database, Migration);

        // The version deployed before does not know them: the checkouts it places have no dispute.
        var now = DateTimeOffset.UtcNow;
        Guid laterCheckout;
        await using (var previous = BeforeOnlinePaymentReconciliation.CreateContext(database, seed.TenantId))
        {
            var checkout = OnlineCheckout.Start(seed.TenantId, ProviderUserId, 40m, "{\"items\":[]}", null, null, now);
            previous.OnlineCheckouts.Add(checkout);
            await previous.SaveChangesAsync();
            laterCheckout = checkout.Id;
        }

        await using (var context = database.CreateContext(seed.TenantId))
        {
            Assert.All(await context.OnlineCheckouts.ToListAsync(), checkout =>
                Assert.Equal((OnlinePaymentDispute.None, (DateTimeOffset?)null), (checkout.Dispute, checkout.DisputeUpdatedAt)));
            Assert.Null((await context.OnlinePaymentAccounts.SingleAsync()).PaymentsReviewedUntil);

            // What Mercado Pago reports from now on is kept: a dispute, a refund made there, and until when it was reviewed.
            var placed = await context.OnlineCheckouts.SingleAsync(c => c.Id == seed.CheckoutId);
            placed.RecordDispute(OnlinePaymentDispute.Open, now);
            var order = await context.Orders.Include(o => o.Payments).Include(o => o.Refunds).SingleAsync(o => o.Id == seed.OrderId);
            var refund = order.RegisterRefund(20m, OrderPaymentMethod.Online, "Devuelto en Mercado Pago", now, "Mercado Pago", null, "4456", false, []);
            context.OnlinePaymentRefunds.Add(OnlinePaymentRefund.MadeInMercadoPago(
                seed.TenantId, order.Id, refund.Id, "98765", 20m, "4456", OnlinePaymentRefundReason.InMercadoPago, now));
            (await context.OnlinePaymentAccounts.SingleAsync()).ReviewPaymentsUntil(now);
            await context.SaveChangesAsync();
        }

        await using var check = database.CreateContext(seed.TenantId);
        var stored = await check.OnlineCheckouts.SingleAsync(c => c.Id == seed.CheckoutId);
        Assert.Equal(OnlinePaymentDispute.Open, stored.Dispute);
        Assert.NotNull(stored.DisputeUpdatedAt);
        Assert.Equal(OnlinePaymentDispute.None, (await check.OnlineCheckouts.SingleAsync(c => c.Id == laterCheckout)).Dispute);
        var made = await check.OnlinePaymentRefunds.SingleAsync(r => r.Reason == OnlinePaymentRefundReason.InMercadoPago);
        Assert.Equal((OnlinePaymentRefundStatus.Completed, "4456", 20m), (made.Status, made.ProviderRefundId, made.Amount));
        Assert.NotNull((await check.OnlinePaymentAccounts.SingleAsync()).PaymentsReviewedUntil);
    }

    [SqlServerFact]
    public async Task Database_ShouldRecordEachRefundOfMercadoPagoOnce_AndRefuse_WhatTheDomainNeverWrites()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);

        // The refund of the business already has id 4455 in Mercado Pago: another row with it is the same refund.
        var sameId = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, $"""
            INSERT INTO [OnlinePaymentRefunds] ([Id], [TenantId], [PaymentId], [Amount], [Reason], [OrderId], [OrderRefundId], [Status],
                [RequestedAt], [SettledAt], [ProviderRefundId], [CreatedAt])
            VALUES ({Guid.NewGuid()}, {seed.TenantId}, N'98765', 5, 4, {seed.OrderId}, {seed.SecondOrderRefundId}, 1, SYSDATETIMEOFFSET(),
                SYSDATETIMEOFFSET(), N'4455', SYSDATETIMEOFFSET())
            """));
        Assert.Equal(2601, sameId.Number);
        Assert.Contains("IX_OnlinePaymentRefunds_TenantId_ProviderRefundId", sameId.Message);

        await ExecuteAsync(database, $"""
            INSERT INTO [OnlinePaymentRefunds] ([Id], [TenantId], [PaymentId], [Amount], [Reason], [OrderId], [OrderRefundId], [Status],
                [RequestedAt], [SettledAt], [CreatedAt])
            VALUES ({Guid.NewGuid()}, {seed.TenantId}, N'98765', 5, 6, {seed.OrderId}, {seed.SecondOrderRefundId}, 1, SYSDATETIMEOFFSET(),
                SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())
            """);
        var secondChargeback = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, $"""
            UPDATE [OnlinePaymentRefunds] SET [Reason] = 6, [ProviderRefundId] = NULL WHERE [OrderRefundId] = {seed.OrderRefundId}
            """));
        Assert.Equal(2601, secondChargeback.Number);
        Assert.Contains("IX_OnlinePaymentRefunds_TenantId_PaymentId_Chargeback", secondChargeback.Message);

        FormattableString[] never =
        [
            // A dispute without when it changed, or on a checkout that placed no order.
            $"UPDATE [OnlineCheckouts] SET [Dispute] = 1 WHERE [Id] = {seed.CheckoutId}",
            $"UPDATE [OnlineCheckouts] SET [Dispute] = 1, [DisputeUpdatedAt] = SYSDATETIMEOFFSET() WHERE [Id] = {seed.AwaitingCheckoutId}",
            $"UPDATE [OnlineCheckouts] SET [Dispute] = 4, [DisputeUpdatedAt] = SYSDATETIMEOFFSET() WHERE [Id] = {seed.CheckoutId}",
            // Made in Mercado Pago: already done, with the id of its refund there.
            $"UPDATE [OnlinePaymentRefunds] SET [Reason] = 4, [ProviderRefundId] = NULL WHERE [OrderRefundId] = {seed.OrderRefundId}",
            $"UPDATE [OnlinePaymentRefunds] SET [Reason] = 5, [Status] = 0, [SettledAt] = NULL WHERE [OrderRefundId] = {seed.OrderRefundId}",
            $"UPDATE [OnlinePaymentRefunds] SET [Reason] = 4, [CheckoutId] = {seed.CheckoutId} WHERE [OrderRefundId] = {seed.OrderRefundId}",
            // Done by the business in its account: it keeps why Mercado Pago refused it.
            $"UPDATE [OnlinePaymentRefunds] SET [Status] = 3 WHERE [OrderRefundId] = {seed.OrderRefundId}",
            $"UPDATE [OnlinePaymentRefunds] SET [Status] = 4 WHERE [OrderRefundId] = {seed.OrderRefundId}"
        ];
        foreach (var sql in never)
            Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, sql))).Number);
    }

    [SqlServerFact]
    public async Task Applying_ShouldStopWithoutChanges_WhenTwoRefundsHaveTheSameMercadoPagoId()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, BeforeReconciliation);
        var seed = await SeedAsync(database);
        await ExecuteAsync(database, $"""
            INSERT INTO [OnlinePaymentRefunds] ([Id], [TenantId], [PaymentId], [Amount], [Reason], [OrderId], [OrderRefundId], [Status],
                [RequestedAt], [SettledAt], [ProviderRefundId], [CreatedAt])
            VALUES ({Guid.NewGuid()}, {seed.TenantId}, N'98765', 5, 3, {seed.OrderId}, {seed.SecondOrderRefundId}, 1, SYSDATETIMEOFFSET(),
                SYSDATETIMEOFFSET(), N'4455', SYSDATETIMEOFFSET())
            """);

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, Migration));

        Assert.Equal(50036, duplicate.Number);
        await using var context = database.CreateContext();
        Assert.Equal(BeforeReconciliation, await LatestMigrationAsync(context));
        Assert.Equal(0, await CountAsync(context,
            $"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID(N'dbo.OnlineCheckouts') AND [name] = N'Dispute'"));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceAnythingCameFromMercadoPago_AndThenDropOnlyWhatItAdded()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);

        // Each trace stops it: a dispute, a refused refund the business made in its account, and each refund from Mercado Pago.
        FormattableString[] traces =
        [
            $"UPDATE [OnlineCheckouts] SET [Dispute] = 1, [DisputeUpdatedAt] = SYSDATETIMEOFFSET() WHERE [Id] = {seed.CheckoutId}",
            $"UPDATE [OnlineCheckouts] SET [Dispute] = 3, [DisputeUpdatedAt] = SYSDATETIMEOFFSET() WHERE [Id] = {seed.CheckoutId}",
            $"UPDATE [OnlinePaymentRefunds] SET [Status] = 3, [FailureMessage] = N'Sin saldo' WHERE [OrderRefundId] = {seed.OrderRefundId}",
            $"UPDATE [OnlinePaymentRefunds] SET [Reason] = 4 WHERE [OrderRefundId] = {seed.OrderRefundId}",
            $"UPDATE [OnlinePaymentRefunds] SET [Reason] = 5 WHERE [OrderRefundId] = {seed.OrderRefundId}",
            $"UPDATE [OnlinePaymentRefunds] SET [Reason] = 6, [ProviderRefundId] = NULL WHERE [OrderRefundId] = {seed.OrderRefundId}"
        ];
        foreach (var trace in traces)
        {
            await using (var context = database.CreateContext())
            {
                Assert.Equal(Migration, await LatestMigrationAsync(context));
                await context.Database.ExecuteSqlRawAsync(
                    "UPDATE [OnlineCheckouts] SET [Dispute] = 0, [DisputeUpdatedAt] = NULL; " +
                    "UPDATE [OnlinePaymentRefunds] SET [Reason] = 3, [Status] = 1, [FailureMessage] = NULL, [ProviderRefundId] = N'4455';");
            }
            await ExecuteAsync(database, trace);

            var used = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeReconciliation));
            Assert.Equal(50035, used.Number);
            Assert.Contains("Refunds and disputes from Mercado Pago", used.Message);
        }

        // A review of the account is not one of them: it does not stop it.
        await using (var context = database.CreateContext())
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE [OnlineCheckouts] SET [Dispute] = 0, [DisputeUpdatedAt] = NULL; " +
                "UPDATE [OnlinePaymentRefunds] SET [Reason] = 3, [Status] = 1, [FailureMessage] = NULL, [ProviderRefundId] = N'4455'; " +
                "UPDATE [OnlinePaymentAccounts] SET [PaymentsReviewedUntil] = SYSDATETIMEOFFSET();");
        }

        await MigrateAsync(database, BeforeReconciliation);
        await using (var context = database.CreateContext())
        {
            Assert.Equal(BeforeReconciliation, await LatestMigrationAsync(context));
            Assert.Equal((0, 0, 0, 1, 2), (
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.columns
                    WHERE ([object_id] = OBJECT_ID(N'dbo.OnlineCheckouts') AND [name] IN (N'Dispute', N'DisputeUpdatedAt'))
                        OR ([object_id] = OBJECT_ID(N'dbo.OnlinePaymentAccounts') AND [name] = N'PaymentsReviewedUntil')
                    """),
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.check_constraints
                    WHERE [name] IN (N'CK_OnlinePaymentRefunds_MadeInMercadoPago', N'CK_OnlineCheckouts_Dispute')
                    """),
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.indexes
                    WHERE [name] IN (N'IX_OnlinePaymentRefunds_TenantId_ProviderRefundId', N'IX_OnlinePaymentRefunds_TenantId_PaymentId_Chargeback',
                        N'IX_OnlineCheckouts_TenantId_Dispute')
                    """),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [OnlinePaymentRefunds]"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [OnlineCheckouts]")));
            // The checks are back to the previous version's: no refund status 3.
            Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(
                "UPDATE [OnlinePaymentRefunds] SET [Status] = 3, [FailureMessage] = N'Sin saldo'"))).Number);
        }

        // Rolling back again does nothing, and forward again every checkout has no dispute.
        await MigrateAsync(database, BeforeReconciliation);
        await MigrateAsync(database, Migration);
        await using var forward = database.CreateContext(seed.TenantId);
        Assert.All(await forward.OnlineCheckouts.ToListAsync(), checkout => Assert.Equal(OnlinePaymentDispute.None, checkout.Dispute));
    }

    private sealed record Seed(Guid TenantId, Guid OrderId, Guid CheckoutId, Guid AwaitingCheckoutId, Guid OrderRefundId, Guid SecondOrderRefundId);

    /// <summary>
    /// A business with its Mercado Pago account, an order paid online with two «En línea» refunds (the first given back by
    /// Mercado Pago with id 4455), the checkout that placed it and another awaiting payment, as the previous version stores them.
    /// </summary>
    private static async Task<Seed> SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = BeforeOnlinePaymentReconciliation.CreateContext(database);
        var slug = $"cafe-{Guid.NewGuid():N}"[..20];
        var tenant = new Tenant("Café Luna", slug, BusinessType.Food, $"{slug}@demo.com");
        var owner = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"{slug}@demo.com",
            NormalizedUserName = $"{slug}@DEMO.COM",
            Email = $"{slug}@demo.com",
            NormalizedEmail = $"{slug}@DEMO.COM",
            FirstName = "Ana",
            LastName = "Ruiz",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Role = UserRole.TenantAdmin,
            IsActive = true
        };
        var now = DateTimeOffset.UtcNow;
        var order = new Order(tenant.Id, null, null, true, "ORD-000001", BusinessType.Food, OrderType.Retail, "Mariana", null, null, null,
            pricesIncludeTax: true, channel: OrderChannel.Online);
        order.AddItem(Guid.NewGuid(), "Latte", 1, 63.80m);
        order.RegisterOnlinePayment("98765", now, null);
        var first = order.RegisterRefund(10m, OrderPaymentMethod.Online, "Sin crema", now, "Ana", null, null, false, []);
        var second = order.RegisterRefund(5m, OrderPaymentMethod.Online, "Sin azúcar", now, "Ana", null, null, false, []);
        var account = OnlinePaymentAccount.Connect(tenant.Id, ProviderUserId, "v1.access", "v1.refresh", now.AddDays(180), true, owner.Id, now);
        var placed = OnlineCheckout.Start(tenant.Id, ProviderUserId, 63.80m, "{\"items\":[]}", null, "+525522107765", now);
        placed.AttachPreference("pref-1", "https://www.mercadopago.com.mx/checkout/v1/redirect?pref_id=pref-1");
        placed.Place("98765", now, order.Id);
        var awaiting = OnlineCheckout.Start(tenant.Id, ProviderUserId, 20m, "{\"items\":[]}", null, null, now);
        var refund = OnlinePaymentRefund.ForOrder(tenant.Id, order.Id, first.Id, "98765", 10m, now);
        refund.Complete("4455", now);

        context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food));
        context.Users.Add(owner);
        context.Orders.Add(order);
        context.OnlinePaymentAccounts.Add(account);
        context.OnlineCheckouts.AddRange(placed, awaiting);
        context.OnlinePaymentRefunds.Add(refund);
        await context.SaveChangesAsync();
        return new Seed(tenant.Id, order.Id, placed.Id, awaiting.Id, first.Id, second.Id);
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
