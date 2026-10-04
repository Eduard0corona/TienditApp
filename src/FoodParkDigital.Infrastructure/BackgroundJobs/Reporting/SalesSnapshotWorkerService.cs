using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Reports.ReadModels;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.BackgroundJobs.Reporting;

/// <summary>
/// Stores the daily sales of each active business once its days close (see <see cref="DailySalesSnapshotBuilder"/>).
/// Reports read the stored days and compute the rest, so a pass that is late or fails only makes them slower.
/// </summary>
public sealed class SalesSnapshotWorkerService : BackgroundService
{
    private const string WorkerActor = "sales-snapshots";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly BackgroundJobOptions.SalesSnapshotOptions _options;
    private readonly ILogger<SalesSnapshotWorkerService> _logger;

    public SalesSnapshotWorkerService(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider clock,
        IOptions<BackgroundJobOptions> options,
        ILogger<SalesSnapshotWorkerService> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _options = options.Value.SalesSnapshots;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));
        try
        {
            // Startup work (migrations, the other workers) goes first.
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, _options.StartupDelaySeconds)), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var stored = await RunPassAsync(stoppingToken);
                if (stored > 0)
                    _logger.LogInformation("Stored the daily sales of {Days} closed days", stored);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(LogRedaction.RedactDatabaseMessages(ex), "Daily sales snapshot pass failed");
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

    /// <returns>Days stored in this pass, for every business.</returns>
    internal async Task<int> RunPassAsync(CancellationToken cancellationToken)
    {
        List<(Guid Id, DateTimeOffset CreatedAt)> tenants;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            tenants = (await context.Tenants
                    .AsNoTracking()
                    .Where(x => x.Status == TenantStatus.Active)
                    .Select(x => new { x.Id, x.CreatedAt })
                    .ToListAsync(cancellationToken))
                .Select(x => (x.Id, x.CreatedAt))
                .ToList();
        }

        var stored = 0;
        foreach (var (tenantId, createdAt) in tenants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var scope = CreateTenantScope(tenantId);
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var timeZoneId = await context.TenantSettings
                    .AsNoTracking()
                    .Where(x => x.TenantId == tenantId)
                    .Select(x => x.TimeZone)
                    .FirstOrDefaultAsync(cancellationToken);
                var zone = TenantDateTimeResolver.Resolve(timeZoneId);
                var now = _clock.UtcNow;
                var horizon = DailySalesReadModel.LocalToday(zone, now).AddDays(-Math.Max(1, _options.HorizonDays));
                var created = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(createdAt, zone).DateTime);
                var firstDay = created > horizon ? created : horizon;

                stored += await DailySalesSnapshotBuilder.MaterializeAsync(
                    context, tenantId, zone, firstDay, now, _options.MaxDaysPerPass, cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Another instance stored some of these days first (unique key); the next pass looks again.
                _logger.LogInformation("Daily sales of tenant {TenantId} were stored by another instance first", tenantId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(LogRedaction.RedactDatabaseMessages(ex), "Daily sales snapshots of tenant {TenantId} failed", tenantId);
            }
        }

        return stored;
    }

    private AsyncServiceScope CreateTenantScope(Guid tenantId)
    {
        var scope = _scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantService>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<IBackgroundExecutionContext>().SetActor(null, WorkerActor);
        return scope;
    }
}
