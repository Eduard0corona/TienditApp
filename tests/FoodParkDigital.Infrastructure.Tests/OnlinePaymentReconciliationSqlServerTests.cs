using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.OnlinePayments;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// What happens in Mercado Pago reaches the order (Fase 24K, ADR 0044) in SQL Server set up as in production: the
/// notification and the review reflecting the same payment at once record each refund of Mercado Pago once, and a lost
/// chargeback once, because the database refuses a second one (its unique indexes and the order's version).
/// </summary>
public class OnlinePaymentReconciliationSqlServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 17, 0, 0, TimeSpan.Zero);
    private const long AccountId = 987654321;
    private const string PaymentId = "1234567890";

    [SqlServerFact]
    public async Task SameRefundOfMercadoPago_ReflectedByManyPathsAtOnce_ShouldBeRecordedOnce()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        var payment = Payment(seed.CheckoutId, "approved", "partially_refunded", refunded: 20m);
        IReadOnlyList<OnlinePaymentProviderRefund> refunds = [new("4455", 20m, "approved", "collector", Now.AddHours(1))];

        var outcomes = await RaceAsync(database, seed.TenantId, new ReconcileOnlinePaymentCommand(seed.CheckoutId, payment, refunds));

        // Each one either recorded it, found it recorded, or lost the race to the database and would read it on its retry.
        Assert.Equal(1, outcomes.Count(o => o is { RefundsRecorded: 1 }));
        Assert.All(outcomes, o => Assert.True(o is null || o.RefundsRecorded <= 1));
        var retry = await ReconcileAsync(database, seed.TenantId, new ReconcileOnlinePaymentCommand(seed.CheckoutId, payment, refunds));
        Assert.False(retry.ChangedAnything);

        await using var check = database.CreateContext(seed.TenantId);
        var made = await check.OnlinePaymentRefunds.SingleAsync();
        Assert.Equal((OnlinePaymentRefundReason.InMercadoPago, "4455", 20m), (made.Reason, made.ProviderRefundId, made.Amount));
        var order = await check.Orders.Include(o => o.Payments).Include(o => o.Refunds).SingleAsync();
        Assert.Equal((20m, 90m), (Assert.Single(order.Refunds).Amount, order.OnlineRefundable));
    }

    [SqlServerFact]
    public async Task LostChargeback_ReflectedByManyPathsAtOnce_ShouldGiveBackOnce()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        var seed = await SeedAsync(database);
        var payment = Payment(seed.CheckoutId, OnlinePaymentDetails.ChargedBackStatus, "settled", refunded: 0m);

        var outcomes = await RaceAsync(database, seed.TenantId, new ReconcileOnlinePaymentCommand(seed.CheckoutId, payment, []));

        Assert.Equal(1, outcomes.Count(o => o is { RefundsRecorded: 1 }));
        await using var check = database.CreateContext(seed.TenantId);
        var chargeback = await check.OnlinePaymentRefunds.SingleAsync();
        Assert.Equal((OnlinePaymentRefundReason.Chargeback, 110m), (chargeback.Reason, chargeback.Amount));
        Assert.Equal(OnlinePaymentDispute.Lost, (await check.OnlineCheckouts.SingleAsync()).Dispute);
        Assert.Equal(0m, (await check.Orders.Include(o => o.Payments).Include(o => o.Refunds).SingleAsync()).OnlineRefundable);
    }

    /// <summary>Five paths reflect the same payment at once, each with its own connection, as the worker and the inbox do.</summary>
    /// <returns>What each one did; null for one the database refused (it lost the race).</returns>
    private static async Task<OnlinePaymentReconciliation?[]> RaceAsync(
        SqlServerTestDatabase database, Guid tenantId, ReconcileOnlinePaymentCommand command)
        => await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            try
            {
                return await ReconcileAsync(database, tenantId, command);
            }
            catch (DbUpdateException)
            {
                return null;
            }
        }));

    private static async Task<OnlinePaymentReconciliation> ReconcileAsync(
        SqlServerTestDatabase database, Guid tenantId, ReconcileOnlinePaymentCommand command)
    {
        await using var context = database.CreateContext(tenantId);
        var handler = new ReconcileOnlinePaymentCommandHandler(
            context, new LiveGateway(), new FixedClock(), NullLogger<ReconcileOnlinePaymentCommandHandler>.Instance);
        return (await handler.Handle(command, CancellationToken.None)).Value;
    }

    private static OnlinePaymentDetails Payment(Guid checkoutId, string status, string detail, decimal refunded)
        => new(PaymentId, status, detail, checkoutId.ToString(), 110m, "MXN", Now, AccountId, LiveMode: true, refunded, Now.AddHours(1));

    private sealed record Seed(Guid TenantId, Guid CheckoutId);

    /// <summary>A business with its account, and an order of $110 paid online by the checkout that placed it.</summary>
    private static async Task<Seed> SeedAsync(SqlServerTestDatabase database)
    {
        var slug = $"cafe-{Guid.NewGuid():N}"[..20];
        var tenant = new Tenant("Café Luna", slug, BusinessType.Food, $"{slug}@demo.com");
        var order = new Order(tenant.Id, null, null, true, "ORD-000001", BusinessType.Food, OrderType.Retail, "Ana", null, null, null,
            pricesIncludeTax: true, channel: OrderChannel.Online);
        order.AddItem(Guid.NewGuid(), "Latte", 2, 55m);
        order.RegisterOnlinePayment(PaymentId, Now, null);
        var account = OnlinePaymentAccount.Connect(tenant.Id, AccountId, "v1.access", "v1.refresh", Now.AddDays(180), true, Guid.NewGuid(), Now);
        var checkout = OnlineCheckout.Start(tenant.Id, AccountId, 110m, "{\"items\":[]}", null, null, Now);
        checkout.Place(PaymentId, Now, order.Id);

        await using var context = database.CreateContext();
        context.AddRange(tenant, new TenantSettings(tenant.Id, TenantCapability.Food));
        context.Orders.Add(order);
        context.OnlinePaymentAccounts.Add(account);
        context.OnlineCheckouts.Add(checkout);
        await context.SaveChangesAsync();
        return new Seed(tenant.Id, checkout.Id);
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => Now.AddDays(1);
    }

    /// <summary>Only whether the platform works with real money: the command reads nothing from Mercado Pago itself.</summary>
    private sealed class LiveGateway : IOnlinePaymentGateway
    {
        public bool IsConfigured => true;
        public bool Sandbox => false;
        public Uri AuthorizationUrl(Guid state, string codeChallenge) => throw new NotSupportedException();
        public Task<OnlinePaymentCredentials> ConnectAsync(string code, string codeVerifier, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OnlinePaymentCredentials> RenewAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OnlinePaymentCheckoutPage> CreateCheckoutAsync(string accessToken, OnlinePaymentCheckoutRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OnlinePaymentDetails?> GetPaymentAsync(string accessToken, string paymentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<OnlinePaymentDetails>> FindPaymentsAsync(string accessToken, Guid checkoutId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<OnlinePaymentDetails>> FindChangedPaymentsAsync(string accessToken, DateTimeOffset from, DateTimeOffset to, int offset, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<OnlinePaymentProviderRefund>> GetRefundsAsync(string accessToken, string paymentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OnlinePaymentRefundOutcome> RefundAsync(string accessToken, string paymentId, decimal amount, Guid idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
