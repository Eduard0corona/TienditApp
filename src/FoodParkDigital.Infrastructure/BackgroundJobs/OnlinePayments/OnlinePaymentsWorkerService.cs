using System.Diagnostics;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.OnlinePayments;
using FoodParkDigital.Domain.OnlinePayments.Entities;
using FoodParkDigital.Domain.OnlinePayments.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.BackgroundJobs.OnlinePayments;

/// <summary>
/// The third way TienditApp learns of an online payment (Fase 24I, ADR 0031), besides the notification and the customer's
/// return: every few minutes it sweeps each business with payments in course, or whose Mercado Pago key is close to
/// expiring (<see cref="SweepOnlinePaymentsCommand"/>), in that business's scope. Since the Fase 24K (ADR 0044) it also
/// reviews, every <see cref="ReviewOnlinePaymentsCommandHandler.ReviewEvery"/>, the payments that changed in each connected
/// account (<see cref="ReviewOnlinePaymentsCommand"/>), so a refund made in Mercado Pago or a dispute reaches the order
/// even without its notification. Without Mercado Pago configured it does nothing.
/// </summary>
public sealed class OnlinePaymentsWorkerService : BackgroundService
{
    private const string WorkerActor = "online-payments";

    /// <summary>
    /// The reviews of a pass start within this long (Fase 24K): the sweep of the next pass (payments in course, keys to
    /// renew) never waits behind a first review of many accounts. The accounts left go first in the next pass.
    /// </summary>
    internal static readonly TimeSpan MaxReviewTimePerPass = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly IOnlinePaymentGateway _gateway;
    private readonly BackgroundJobOptions.OnlinePaymentOptions _options;
    private readonly ILogger<OnlinePaymentsWorkerService> _logger;

    public OnlinePaymentsWorkerService(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider clock,
        IOnlinePaymentGateway gateway,
        IOptions<BackgroundJobOptions> options,
        ILogger<OnlinePaymentsWorkerService> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _gateway = gateway;
        _options = options.Value.OnlinePayments;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPassAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(LogRedaction.RedactDatabaseMessages(ex), "Online payments sweep failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <returns>Businesses swept or reviewed.</returns>
    internal async Task<int> RunPassAsync(CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return 0;

        List<Guid> tenants;
        List<Guid> reviewing;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = _clock.UtcNow;
            var renewBy = now + OnlinePaymentAccount.RenewBefore;
            var max = Math.Max(1, _options.MaxBusinessesPerPass);
            // Only to find the businesses with work: the sweep itself runs in each business's scope. The one whose payment
            // has waited longest goes first, so a busy platform never leaves the same businesses out of every pass.
            var withCheckouts = await context.OnlineCheckouts
                .IgnoreQueryFilters()
                .Where(c => c.Status == OnlineCheckoutStatus.AwaitingPayment)
                .GroupBy(c => c.TenantId)
                .OrderBy(g => g.Min(c => c.StartedAt))
                .Select(g => g.Key)
                .Take(max)
                .ToListAsync(cancellationToken);
            var renewing = await context.OnlinePaymentAccounts
                .IgnoreQueryFilters()
                .Where(a => a.AccessTokenExpiresAt > now && a.AccessTokenExpiresAt <= renewBy)
                .OrderBy(a => a.AccessTokenExpiresAt)
                .Select(a => a.TenantId)
                .Take(max)
                .ToListAsync(cancellationToken);
            tenants = withCheckouts.Concat(renewing).Distinct().Take(max).ToList();
            // The accounts whose payments were not reviewed lately and whose key works, the longest waiting (or never
            // reviewed) first.
            var reviewedBy = ReviewOnlinePaymentsCommandHandler.DueIfReviewedBefore(now);
            reviewing = await context.OnlinePaymentAccounts
                .IgnoreQueryFilters()
                .Where(a => a.AccessTokenExpiresAt > now && (a.PaymentsReviewedUntil == null || a.PaymentsReviewedUntil <= reviewedBy))
                .OrderBy(a => a.PaymentsReviewedUntil)
                .Select(a => a.TenantId)
                .Take(max)
                .ToListAsync(cancellationToken);
        }

        foreach (var tenantId in tenants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var scope = _scopeFactory.CreateTenantScope(tenantId, WorkerActor);
                var sweep = await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new SweepOnlinePaymentsCommand(), cancellationToken);
                if (sweep.IsSuccess && (sweep.Value.Expired > 0 || sweep.Value.KeyRenewed))
                    _logger.LogInformation(
                        "Online payments of tenant {TenantId}: {Expired} expired, key renewed {KeyRenewed}",
                        tenantId, sweep.Value.Expired, sweep.Value.KeyRenewed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(LogRedaction.RedactDatabaseMessages(ex), "Online payments sweep of tenant {TenantId} failed", tenantId);
            }
        }

        var reviewed = new List<Guid>();
        var reviewTime = Stopwatch.StartNew();
        foreach (var tenantId in reviewing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reviewTime.Elapsed > MaxReviewTimePerPass)
            {
                _logger.LogInformation(
                    "Online payments review: {Left} accounts left for the next pass", reviewing.Count - reviewed.Count);
                break;
            }
            reviewed.Add(tenantId);
            try
            {
                await using var scope = _scopeFactory.CreateTenantScope(tenantId, WorkerActor);
                var review = await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new ReviewOnlinePaymentsCommand(), cancellationToken);
                if (review.IsSuccess && review.Value.Reflected > 0)
                    _logger.LogInformation(
                        "Online payments of tenant {TenantId}: {Reflected} payments reached their orders", tenantId, review.Value.Reflected);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(LogRedaction.RedactDatabaseMessages(ex), "Online payments review of tenant {TenantId} failed", tenantId);
            }
        }

        return tenants.Union(reviewed).Count();
    }
}
