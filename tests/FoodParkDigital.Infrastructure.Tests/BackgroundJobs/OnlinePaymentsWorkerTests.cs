using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.OnlinePayments;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.BackgroundJobs.OnlinePayments;
using FoodParkDigital.Infrastructure.Persistence;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Infrastructure.Tests.BackgroundJobs;

/// <summary>
/// The online payments worker (Fases 24I and 24K) finds the businesses with work across all of them and sends each one its
/// command in its own scope: since the Fase 24K, the review of the payments that changed in Mercado Pago, every 15 minutes
/// per account whose key works, the longest waiting first, within a time per pass.
/// </summary>
public class OnlinePaymentsWorkerTests
{
    [Fact]
    public void EachKindOfWork_ShouldGetItsTurn_InAPass()
    {
        Guid[] payments = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        Guid[] keys = [Guid.NewGuid()];
        Guid[] deposits = [Guid.NewGuid(), Guid.NewGuid()];

        // Fase 31O: a busy hour of payments does not leave the keys to renew or the deposits to give back out of a pass.
        Assert.Equal([payments[0], keys[0], deposits[0], payments[1], deposits[1], payments[2]],
            OnlinePaymentsWorkerService.InTurns(payments, keys, deposits));
        Assert.Equal(payments, OnlinePaymentsWorkerService.InTurns(payments, [], []));
    }

    [Fact]
    public async Task Pass_ShouldReview_TheAccountsNotReviewedLately_WhoseKeyWorks_InTheirOwnScope()
    {
        var sent = new SentCommands();
        await using var host = OutboxTestHost.Create(configure: services =>
        {
            services.AddSingleton<IOnlinePaymentGateway, ConfiguredGateway>();
            services.AddSingleton(sent);
            services.AddScoped<ISender, RecordingSender>();
            services.AddSingleton<OnlinePaymentsWorkerService>();
        });
        var now = host.Clock.UtcNow;
        var never = await AddAccountAsync(host, "nunca", reviewedUntil: null, keyExpiresAt: now.AddDays(100));
        // A review that read everything leaves the account reviewed until 10 minutes before it ran: due 15 minutes later.
        var stale = await AddAccountAsync(host, "hace-rato", reviewedUntil: now.AddMinutes(-25), keyExpiresAt: now.AddDays(100));
        await AddAccountAsync(host, "reciente", reviewedUntil: now.AddMinutes(-24), keyExpiresAt: now.AddDays(100));
        await AddAccountAsync(host, "sin-llave", reviewedUntil: null, keyExpiresAt: now.AddMinutes(-1));
        var worker = host.Get<OnlinePaymentsWorkerService>();

        var businesses = await worker.RunPassAsync(CancellationToken.None);

        Assert.Equal(2, businesses);
        Assert.Equal([never, stale], sent.Reviews);
        Assert.Empty(sent.Sweeps);
    }

    [Fact]
    public async Task Pass_ShouldStartWith_TheAccountsThePassBeforeLeftOut_WhenItsReviewsRanOutOfTime()
    {
        // Each review takes longer than the reviews of a pass may start in, and leaves the account as behind as it was (the
        // first review of an account with many sales, or one that keeps failing): each pass reviews only one account.
        var sent = new SentCommands { EachReviewTakes = OnlinePaymentsWorkerService.MaxReviewTimePerPass + TimeSpan.FromSeconds(1) };
        await using var host = OutboxTestHost.Create(configure: services =>
        {
            services.AddSingleton<IOnlinePaymentGateway, ConfiguredGateway>();
            services.AddSingleton(sent);
            services.AddScoped<ISender, RecordingSender>();
            services.AddSingleton<OnlinePaymentsWorkerService>();
        });
        var now = host.Clock.UtcNow;
        var behind = await AddAccountAsync(host, "atrasada", reviewedUntil: now.AddDays(-90), keyExpiresAt: now.AddDays(100));
        var second = await AddAccountAsync(host, "segunda", reviewedUntil: now.AddHours(-2), keyExpiresAt: now.AddDays(100));
        var third = await AddAccountAsync(host, "tercera", reviewedUntil: now.AddHours(-1), keyExpiresAt: now.AddDays(100));
        var worker = host.Get<OnlinePaymentsWorkerService>();

        for (var pass = 0; pass < 4; pass++)
            Assert.Equal(1, await worker.RunPassAsync(CancellationToken.None));

        // The account reviewed longest ago goes first, not the one still reviewed until the oldest date.
        Assert.Equal([behind, second, third, behind], sent.Reviews);
    }

    [Fact]
    public async Task Pass_ShouldSweep_TheBusinessesWithADepositHeldOnARequestWhoseTimeCame_AndNoOther()
    {
        // Fase 31O (ADR 0095): a request booked with a deposit that nobody answered before its time gives it back in the sweep,
        // even in a business with no payment waiting and no key to renew.
        var sent = new SentCommands();
        await using var host = OutboxTestHost.Create(configure: services =>
        {
            services.AddSingleton<IOnlinePaymentGateway, ConfiguredGateway>();
            services.AddSingleton(sent);
            services.AddScoped<ISender, RecordingSender>();
            services.AddSingleton<OnlinePaymentsWorkerService>();
        });
        var now = host.Clock.UtcNow;
        var late = await AddDepositRequestAsync(host, "vencida", now.AddMinutes(-30));
        var onTime = await AddDepositRequestAsync(host, "hora-justa", now);
        // Its time has not come, the business accepted it, its deposit went back already, or it asks none: no sweep.
        await AddDepositRequestAsync(host, "futura", now.AddMinutes(1));
        await AddDepositRequestAsync(host, "confirmada", now.AddMinutes(-30), accepted: true);
        await AddDepositRequestAsync(host, "devuelta", now.AddMinutes(-30), returned: true);
        await AddDepositRequestAsync(host, "sin-anticipo", now.AddMinutes(-30), deposit: null);
        var worker = host.Get<OnlinePaymentsWorkerService>();

        Assert.Equal(2, await worker.RunPassAsync(CancellationToken.None));

        // The one waiting longest first, each in its own scope.
        Assert.Equal([late, onTime], sent.Sweeps);
        Assert.Empty(sent.Reviews);
    }

    private static Task<Guid> AddDepositRequestAsync(
        OutboxTestHost host, string slug, DateTimeOffset startAt, decimal? deposit = 45m, bool accepted = false, bool returned = false)
        => host.InScopeAsync(async services =>
        {
            var tenant = new Tenant($"Negocio {slug}", slug, BusinessType.Service, $"{slug}@demo.com");
            services.GetRequiredService<ICurrentTenantService>().SetTenant(tenant.Id);
            var context = services.GetRequiredService<ApplicationDbContext>();
            var request = Appointment.RequestOnline(
                tenant.Id, "Valeria Ruiz", "+525522107765", Guid.NewGuid(), "Corte de cabello", startAt, startAt.AddMinutes(45),
                null, null, Guid.NewGuid(), new string(slug[0], 43), deposit: deposit);
            if (accepted)
                request.Accept(startAt.AddDays(-1));
            if (returned)
                request.ReturnDeposit();
            context.Tenants.Add(tenant);
            context.Appointments.Add(request);
            // The row version SQL Server writes.
            context.Entry(request).Property("Version").CurrentValue = new byte[8];
            await context.SaveChangesAsync();
            return tenant.Id;
        });

    private static Task<Guid> AddAccountAsync(OutboxTestHost host, string slug, DateTimeOffset? reviewedUntil, DateTimeOffset keyExpiresAt)
        => host.InScopeAsync(async services =>
        {
            var tenant = new Tenant($"Negocio {slug}", slug, BusinessType.Food, $"{slug}@demo.com");
            services.GetRequiredService<ICurrentTenantService>().SetTenant(tenant.Id);
            var context = services.GetRequiredService<ApplicationDbContext>();
            var connectedAt = keyExpiresAt.AddDays(-180);
            var account = OnlinePaymentAccount.Connect(
                tenant.Id, 1000 + slug.Length, "access", "refresh", keyExpiresAt, true, Guid.NewGuid(), connectedAt);
            if (reviewedUntil is { } until)
                account.ReviewPaymentsUntil(until);
            context.Tenants.Add(tenant);
            context.OnlinePaymentAccounts.Add(account);
            await context.SaveChangesAsync();
            return tenant.Id;
        });

    private sealed class SentCommands
    {
        public List<Guid> Reviews { get; } = [];
        public List<Guid> Sweeps { get; } = [];
        /// <summary>How long the clock moves with each review.</summary>
        public TimeSpan EachReviewTakes { get; init; }
    }

    /// <summary>Records each command with the business whose scope sent it.</summary>
    private sealed class RecordingSender(SentCommands sent, ICurrentTenantService tenant, MutableClock clock) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var business = tenant.TenantId;
            if (request is ReviewOnlinePaymentsCommand)
                clock.Advance(sent.EachReviewTakes);
            object result = request switch
            {
                ReviewOnlinePaymentsCommand => Record(sent.Reviews, business, Result.Success(new OnlinePaymentReview(0, 0, null))),
                SweepOnlinePaymentsCommand => Record(sent.Sweeps, business, Result.Success(new OnlinePaymentSweep(0, 0, false))),
                _ => throw new NotSupportedException(request.GetType().Name)
            };
            return Task.FromResult((TResponse)result);
        }

        private static object Record(List<Guid> list, Guid business, object result)
        {
            list.Add(business);
            return result;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ConfiguredGateway : IOnlinePaymentGateway
    {
        public bool IsConfigured => true;
        public bool Sandbox => false;
        public Uri AuthorizationUrl(Guid state, string codeChallenge) => throw new NotSupportedException();
        public Task<OnlinePaymentCredentials> ConnectAsync(string code, string codeVerifier, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OnlinePaymentCredentials> RenewAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OnlinePaymentCheckoutPage> CreateCheckoutAsync(string accessToken, OnlinePaymentCheckoutRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OnlinePaymentDetails?> GetPaymentAsync(string accessToken, string paymentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<OnlinePaymentDetails>> FindPaymentsAsync(string accessToken, Guid checkoutId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OnlinePaymentChanges> FindChangedPaymentsAsync(string accessToken, DateTimeOffset from, DateTimeOffset to, int offset, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<OnlinePaymentProviderRefund>> GetRefundsAsync(string accessToken, string paymentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OnlinePaymentRefundOutcome> RefundAsync(string accessToken, string paymentId, decimal amount, Guid idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
