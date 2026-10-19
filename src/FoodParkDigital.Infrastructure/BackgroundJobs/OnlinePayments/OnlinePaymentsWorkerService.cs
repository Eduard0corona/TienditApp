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
/// expiring (<see cref="SweepOnlinePaymentsCommand"/>), in that business's scope. Without Mercado Pago configured it does
/// nothing.
/// </summary>
public sealed class OnlinePaymentsWorkerService : BackgroundService
{
    private const string WorkerActor = "online-payments";

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

    /// <returns>Businesses swept.</returns>
    internal async Task<int> RunPassAsync(CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
            return 0;

        List<Guid> tenants;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = _clock.UtcNow;
            var renewBy = now + OnlinePaymentAccount.RenewBefore;
            // Only to find the businesses with work: the sweep itself runs in each business's scope.
            tenants = await context.OnlineCheckouts
                .IgnoreQueryFilters()
                .Where(c => c.Status == OnlineCheckoutStatus.AwaitingPayment)
                .Select(c => c.TenantId)
                .Union(context.OnlinePaymentAccounts
                    .IgnoreQueryFilters()
                    .Where(a => a.AccessTokenExpiresAt > now && a.AccessTokenExpiresAt <= renewBy)
                    .Select(a => a.TenantId))
                .Distinct()
                .Take(Math.Max(1, _options.MaxBusinessesPerPass))
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

        return tenants.Count;
    }
}
