using FoodParkDigital.Application.Billing;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.BackgroundJobs.Billing;

/// <summary>
/// Applies the billing dates to the businesses (see <see cref="SubscriptionStatusRefresher"/>): past due when the
/// trial or the paid period ends, suspended after the grace days. Each business is refreshed in its own scope.
/// </summary>
public sealed class SubscriptionStatusWorkerService : BackgroundService
{
    private const string WorkerActor = "billing";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly BackgroundJobOptions.SubscriptionOptions _options;
    private readonly ILogger<SubscriptionStatusWorkerService> _logger;

    public SubscriptionStatusWorkerService(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider clock,
        IOptions<BackgroundJobOptions> options,
        ILogger<SubscriptionStatusWorkerService> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _options = options.Value.Subscriptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var changed = await RunPassAsync(stoppingToken);
                if (changed > 0)
                    _logger.LogInformation("Billing status changed for {Count} businesses", changed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(LogRedaction.RedactDatabaseMessages(ex), "Billing status pass failed");
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

    /// <returns>Businesses whose status changed.</returns>
    internal async Task<int> RunPassAsync(CancellationToken cancellationToken)
    {
        List<Guid> stale;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            stale = await SubscriptionStatusRefresher.FindStaleTenantsAsync(context, _clock.UtcNow, cancellationToken);
        }

        var changed = 0;
        foreach (var tenantId in stale)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var scope = _scopeFactory.CreateTenantScope(tenantId, WorkerActor);
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                if (await SubscriptionStatusRefresher.RefreshAsync(context, tenantId, _clock.UtcNow, cancellationToken))
                    changed++;
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogInformation("Billing status of tenant {TenantId} was changed by someone else first", tenantId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(LogRedaction.RedactDatabaseMessages(ex), "Billing status of tenant {TenantId} failed", tenantId);
            }
        }

        return changed;
    }
}
