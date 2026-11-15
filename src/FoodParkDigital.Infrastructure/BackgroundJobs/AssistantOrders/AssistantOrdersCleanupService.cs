using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.BackgroundJobs.AssistantOrders;

/// <summary>
/// Deletes the orders AI assistants prepared (Fase 32B, ADR 0096) a week after their link expired, sent or not: the order a
/// customer sent is the business's record, and one never sent never reached it. A bot preparing orders leaves nothing
/// behind after that week. Once an hour, in batches.
/// </summary>
public sealed class AssistantOrdersCleanupService : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    public const int KeepDays = 7;
    public const int BatchSize = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AssistantOrdersCleanupService> _logger;

    public AssistantOrdersCleanupService(
        IServiceScopeFactory scopeFactory, IDateTimeProvider clock, ILogger<AssistantOrdersCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await DeleteExpiredAsync(stoppingToken) == BatchSize)
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Typically the database being unreachable: the next pass deletes them.
                _logger.LogError(Services.LogRedaction.RedactDatabaseMessages(ex), "Cleanup of orders prepared by assistants failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One batch of the ones expired more than <see cref="KeepDays"/> days ago; how many it deleted.</summary>
    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var cutoff = _clock.UtcNow.AddDays(-KeepDays);
        // Of every business at once, before any is in context: only the ones whose link expired a week ago, by when.
        var expired = await context.AssistantOrders
            .IgnoreQueryFilters()
            .Where(order => order.ExpiresAt < cutoff)
            .OrderBy(order => order.ExpiresAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        if (expired.Count == 0)
            return 0;

        context.AssistantOrders.RemoveRange(expired);
        await context.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }
}
