using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.OnlinePayments;
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
    }

    /// <summary>Records each command with the business whose scope sent it.</summary>
    private sealed class RecordingSender(SentCommands sent, ICurrentTenantService tenant) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var business = tenant.TenantId;
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
