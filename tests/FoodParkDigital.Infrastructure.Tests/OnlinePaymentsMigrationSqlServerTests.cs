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
/// AddOnlinePayments (Fase 24I, ADR 0031): one Mercado Pago account per business; each checkout status keeps only what it
/// means (the request while awaiting a payment, the payment and the order once placed, the payment and why once not
/// placed), one order per checkout and one automatic refund per approved payment; and rolling back stops with error 50024
/// once a customer paid or started to, and otherwise drops only the four tables and the sales report's online column.
/// </summary>
public class OnlinePaymentsMigrationSqlServerTests
{
    // The migration right before AddOnlinePayments, where its rollback.sql takes the database.
    private const string BeforeOnlinePayments = "20261018141704_AddBusinessReviews";
    private const string Migration = "20261019015524_AddOnlinePayments";
    private const long ProviderUserId = 1234567890;

    [SqlServerFact]
    public async Task Tables_ShouldKeepWhatTheDomainDoes_OneAccountPerBusiness_AndOneOrderPerCheckout()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);

        await using (var context = BeforeCustomerCredit.CreateContext(database))
        {
            var checkout = await context.OnlineCheckouts.IgnoreQueryFilters().SingleAsync(c => c.Id == seed.CheckoutId);
            checkout.AttachPreference("pref-1", "https://www.mercadopago.com.mx/checkout/v1/redirect?pref_id=pref-1");
            checkout.Place("98765", DateTimeOffset.UtcNow, seed.OrderId);
            context.OnlinePaymentRefunds.Add(OnlinePaymentRefund.ForCheckout(
                seed.TenantId, checkout.Id, "98766", checkout.Amount, OnlinePaymentRefundReason.DuplicatePayment, DateTimeOffset.UtcNow));
            await context.SaveChangesAsync();
        }

        await using (var check = BeforeCustomerCredit.CreateContext(database))
        {
            var stored = await check.OnlineCheckouts.IgnoreQueryFilters().SingleAsync(c => c.Id == seed.CheckoutId);
            Assert.Equal((OnlineCheckoutStatus.Placed, seed.OrderId, "98765", (string?)null),
                (stored.Status, stored.OrderId, stored.PaymentId, stored.Request));
            var account = await check.OnlinePaymentAccounts.IgnoreQueryFilters().SingleAsync();
            Assert.Equal(("v1.access", ProviderUserId), (account.ProtectedAccessToken, account.ProviderUserId));
        }

        var secondAccount = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, $"""
            INSERT INTO [OnlinePaymentAccounts] ([Id], [TenantId], [ProviderUserId], [ProtectedAccessToken], [ProtectedRefreshToken],
                [AccessTokenExpiresAt], [LiveMode], [ConnectedAt], [ConnectedByUserId], [AcceptsPayments], [CreatedAt])
            VALUES ({Guid.NewGuid()}, {seed.TenantId}, 987, N'a', N'r', SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), {seed.UserId}, 0, SYSDATETIMEOFFSET())
            """));
        Assert.Equal(2601, secondAccount.Number);
        Assert.Contains("IX_OnlinePaymentAccounts_TenantId", secondAccount.Message);

        var secondCheckout = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, $"""
            INSERT INTO [OnlineCheckouts] ([Id], [TenantId], [ProviderUserId], [Amount], [Status], [StartedAt], [ExpiresAt],
                [PaymentId], [PaidAt], [OrderId], [CreatedAt])
            VALUES ({Guid.NewGuid()}, {seed.TenantId}, 1, 100, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'1', SYSDATETIMEOFFSET(),
                {seed.OrderId}, SYSDATETIMEOFFSET())
            """));
        Assert.Equal(2601, secondCheckout.Number);
        Assert.Contains("IX_OnlineCheckouts_OrderId", secondCheckout.Message);

        var secondRefund = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database, $"""
            INSERT INTO [OnlinePaymentRefunds] ([Id], [TenantId], [PaymentId], [Amount], [Reason], [CheckoutId], [Status], [RequestedAt], [CreatedAt])
            VALUES ({Guid.NewGuid()}, {seed.TenantId}, N'98766', 10, 1, {seed.CheckoutId}, 0, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())
            """));
        Assert.Equal(2601, secondRefund.Number);
        Assert.Contains("IX_OnlinePaymentRefunds_TenantId_PaymentId", secondRefund.Message);

        // A business is never deleted; its payments would not let it.
        var deleteBusiness = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(database,
            $"DELETE FROM [Tenants] WHERE [Id] = {seed.TenantId}"));
        Assert.Equal(547, deleteBusiness.Number);
    }

    [SqlServerFact]
    public async Task Database_ShouldRefuse_WhatACheckoutOrARefundCanNeverBe()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);
        Guid refundId;
        await using (var context = BeforeCustomerCredit.CreateContext(database))
        {
            var refund = OnlinePaymentRefund.ForCheckout(
                seed.TenantId, seed.CheckoutId, "98765", 50m, OnlinePaymentRefundReason.NotPlaced, DateTimeOffset.UtcNow);
            context.OnlinePaymentRefunds.Add(refund);
            await context.SaveChangesAsync();
            refundId = refund.Id;
        }

        foreach (var (update, id, constraint) in new[]
                 {
                     ("UPDATE [OnlineCheckouts] SET [Amount] = 0 WHERE [Id] = {0}", seed.CheckoutId, "CK_OnlineCheckouts_Amount"),
                     ("UPDATE [OnlineCheckouts] SET [Request] = NULL WHERE [Id] = {0}", seed.CheckoutId, "CK_OnlineCheckouts_Outcome"),
                     ("UPDATE [OnlineCheckouts] SET [Status] = 1, [Request] = NULL, [PaymentId] = N'1', [PaidAt] = SYSDATETIMEOFFSET() WHERE [Id] = {0}",
                         seed.CheckoutId, "CK_OnlineCheckouts_Outcome"),
                     ("UPDATE [OnlineCheckouts] SET [Status] = 3, [Request] = NULL, [PaymentId] = N'1', [PaidAt] = SYSDATETIMEOFFSET() WHERE [Id] = {0}",
                         seed.CheckoutId, "CK_OnlineCheckouts_Outcome"),
                     ("UPDATE [OnlineCheckouts] SET [Status] = 3, [Request] = NULL, [PaymentId] = N'1', [PaidAt] = SYSDATETIMEOFFSET(), [Failure] = 5 WHERE [Id] = {0}",
                         seed.CheckoutId, "CK_OnlineCheckouts_Outcome"),
                     ("UPDATE [OnlineCheckouts] SET [Status] = 2 WHERE [Id] = {0}", seed.CheckoutId, "CK_OnlineCheckouts_Outcome"),
                     ("UPDATE [OnlineCheckouts] SET [Status] = 4 WHERE [Id] = {0}", seed.CheckoutId, "CK_OnlineCheckouts_"),
                     ("UPDATE [OnlinePaymentRefunds] SET [Amount] = 0 WHERE [Id] = {0}", refundId, "CK_OnlinePaymentRefunds_Amount"),
                     ("UPDATE [OnlinePaymentRefunds] SET [Reason] = 3 WHERE [Id] = {0}", refundId, "CK_OnlinePaymentRefunds_Link"),
                     ("UPDATE [OnlinePaymentRefunds] SET [CheckoutId] = NULL WHERE [Id] = {0}", refundId, "CK_OnlinePaymentRefunds_Link"),
                     ("UPDATE [OnlinePaymentRefunds] SET [Status] = 1 WHERE [Id] = {0}", refundId, "CK_OnlinePaymentRefunds_Settled"),
                     ("UPDATE [OnlinePaymentRefunds] SET [Status] = 2, [SettledAt] = SYSDATETIMEOFFSET() WHERE [Id] = {0}", refundId, "CK_OnlinePaymentRefunds_Settled"),
                     ("UPDATE [OnlinePaymentAccounts] SET [ProviderUserId] = 0 WHERE [TenantId] = {0}", seed.TenantId, "CK_OnlinePaymentAccounts_ProviderUserId")
                 })
        {
            await using var context = BeforeCustomerCredit.CreateContext(database);
            var error = await Assert.ThrowsAsync<SqlException>(() => context.Database.ExecuteSqlRawAsync(update, id));
            Assert.Equal(547, error.Number);
            Assert.Contains(constraint, error.Message);
        }

        // What the domain does is what the database keeps: a checkout that could not be placed, and its refund done.
        await using (var context = BeforeCustomerCredit.CreateContext(database))
        {
            var checkout = await context.OnlineCheckouts.IgnoreQueryFilters().SingleAsync(c => c.Id == seed.CheckoutId);
            checkout.FailToPlace("98765", DateTimeOffset.UtcNow, OnlineCheckoutFailure.OrderRefused, "El Latte se agotó.");
            var refund = await context.OnlinePaymentRefunds.IgnoreQueryFilters().SingleAsync(r => r.Id == refundId);
            refund.Complete("555", DateTimeOffset.UtcNow);
            await context.SaveChangesAsync();
        }

        await using var check = BeforeCustomerCredit.CreateContext(database);
        var stored = await check.OnlineCheckouts.IgnoreQueryFilters().SingleAsync(c => c.Id == seed.CheckoutId);
        Assert.Equal((OnlineCheckoutStatus.NotPlaced, OnlineCheckoutFailure.OrderRefused, (string?)null),
            (stored.Status, stored.Failure, stored.CustomerPhone));
        Assert.Equal(OnlinePaymentRefundStatus.Completed,
            (await check.OnlinePaymentRefunds.IgnoreQueryFilters().SingleAsync(r => r.Id == refundId)).Status);
    }

    [SqlServerFact]
    public async Task SalesReport_StoredByTheDeployedVersion_ShouldReadNothingPaidOnline()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        var seed = await SeedAsync(database);

        // The version deployed while apply.sql runs (Fase 24H) stores a day without the new column.
        await ExecuteAsync(database, $"""
            INSERT INTO [DailySalesSnapshots] ([Id], [TenantId], [BusinessDate], [OrderType], [TimeZoneId], [ComputedAt],
                [OrdersCreated], [PaidOrders], [PaymentsCount], [PaymentsAmount], [CashAmount], [CardAmount], [TransferAmount],
                [WalletAmount], [OtherAmount], [RefundsCount], [RefundsAmount], [PlatformAmount])
            VALUES ({Guid.NewGuid()}, {seed.TenantId}, '2026-10-18', 0, N'America/Mexico_City', SYSDATETIMEOFFSET(),
                1, 1, 1, 55, 55, 0, 0, 0, 0, 0, 0, 0)
            """);

        await using var check = BeforeCustomerCredit.CreateContext(database);
        var day = await check.DailySalesSnapshots.IgnoreQueryFilters().SingleAsync();
        Assert.Equal((55m, 0m), (day.PaymentsAmount, day.OnlineAmount));
    }

    [SqlServerFact]
    public async Task RollingBack_ShouldStopWithoutChanges_OnceACustomerPaidOnline_AndThenDropOnlyTheTables()
    {
        await using var database = SqlServerTestDatabase.CreateEmpty();
        await MigrateAsync(database, Migration);
        await SeedAsync(database);

        var error = await Assert.ThrowsAsync<SqlException>(() => MigrateAsync(database, BeforeOnlinePayments));
        Assert.Equal(50024, error.Number);
        Assert.Contains("Customers paid online", error.Message);
        await using (var context = BeforeCustomerCredit.CreateContext(database))
        {
            Assert.Equal(Migration, await LatestMigrationAsync(context));
            Assert.Equal(1, await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [OnlineCheckouts]"));
            await context.Database.ExecuteSqlRawAsync("DELETE FROM [OnlineCheckouts]");
        }

        // The connected account alone does not stop it: the business connects it again.
        await MigrateAsync(database, BeforeOnlinePayments);
        await using (var context = BeforeCustomerCredit.CreateContext(database))
        {
            Assert.Equal(BeforeOnlinePayments, await LatestMigrationAsync(context));
            Assert.Equal((0, 0, 1, 1, 1), (
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.tables
                    WHERE [name] IN (N'OnlinePaymentAccounts', N'OnlinePaymentAuthorizations', N'OnlineCheckouts', N'OnlinePaymentRefunds')
                    """),
                await CountAsync(context, $"""
                    SELECT COUNT(*) AS [Value] FROM sys.columns
                    WHERE [object_id] = OBJECT_ID(N'dbo.DailySalesSnapshots') AND [name] = N'OnlineAmount'
                    """),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [AspNetUsers]"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Tenants]"),
                await CountAsync(context, $"SELECT COUNT(*) AS [Value] FROM [Orders]")));
        }

        // Rolling back again does nothing, and forward again the tables are back.
        await MigrateAsync(database, BeforeOnlinePayments);
        await MigrateAsync(database, Migration);
        await using var forward = BeforeCustomerCredit.CreateContext(database);
        Assert.Equal(4, await CountAsync(forward, $"""
            SELECT COUNT(*) AS [Value] FROM sys.tables
            WHERE [name] IN (N'OnlinePaymentAccounts', N'OnlinePaymentAuthorizations', N'OnlineCheckouts', N'OnlinePaymentRefunds')
            """));
    }

    private sealed record Seed(Guid TenantId, Guid UserId, Guid OrderId, Guid CheckoutId);

    /// <summary>A business with its Mercado Pago account, an order and a checkout awaiting payment.</summary>
    private static async Task<Seed> SeedAsync(SqlServerTestDatabase database)
    {
        await using var context = BeforeCustomerCredit.CreateContext(database);
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
        var order = new Order(tenant.Id, null, null, true, "ORD-000001", BusinessType.Food, OrderType.Retail, "Mariana", null, null, null);
        order.AddItem(Guid.NewGuid(), "Latte", 1, 55m);
        var now = DateTimeOffset.UtcNow;
        var account = OnlinePaymentAccount.Connect(tenant.Id, ProviderUserId, "v1.access", "v1.refresh", now.AddDays(180), true, owner.Id, now);
        var checkout = OnlineCheckout.Start(tenant.Id, ProviderUserId, 63.80m, "{\"items\":[]}", null, "+525522107765", now);

        context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food));
        context.Users.Add(owner);
        context.Orders.Add(order);
        context.OnlinePaymentAccounts.Add(account);
        context.OnlineCheckouts.Add(checkout);
        context.OnlinePaymentAuthorizations.Add(OnlinePaymentAuthorization.Start(tenant.Id, owner.Id, new string('v', 64), now));
        await context.SaveChangesAsync();
        return new Seed(tenant.Id, owner.Id, order.Id, checkout.Id);
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
