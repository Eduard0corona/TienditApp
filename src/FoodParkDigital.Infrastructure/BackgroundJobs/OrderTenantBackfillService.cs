using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.BackgroundJobs;

/// <summary>
/// Gives order lines and status history the business of their order when they have none: the ones the previous
/// version added between the migration (ScopeOrderLinesAndHistoryByTenant) and this deployment. Until then the tenant
/// filter hides them. Runs once at start, whether or not background jobs are on; it does nothing once they are done.
/// </summary>
public sealed class OrderTenantBackfillService : BackgroundService
{
    // The same statements as the migration: rows with the empty default take their order's business.
    internal const string Sql = """
        UPDATE i SET i.[TenantId] = o.[TenantId]
        FROM [OrderItems] AS i
        INNER JOIN [Orders] AS o ON o.[Id] = i.[OrderId]
        WHERE i.[TenantId] = '00000000-0000-0000-0000-000000000000';
        UPDATE h SET h.[TenantId] = o.[TenantId]
        FROM [OrderStatusHistory] AS h
        INNER JOIN [Orders] AS o ON o.[Id] = h.[OrderId]
        WHERE h.[TenantId] = '00000000-0000-0000-0000-000000000000';
        """;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderTenantBackfillService> _logger;

    public OrderTenantBackfillService(IServiceScopeFactory scopeFactory, ILogger<OrderTenantBackfillService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var completed = await CompleteAsync(context, stoppingToken);
            if (completed > 0)
                _logger.LogInformation("Gave {Count} order lines and status changes the business of their order.", completed);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(
                LogRedaction.RedactDatabaseMessages(ex),
                "Order lines without a business could not be completed; they stay hidden until the next start.");
        }
    }

    /// <returns>How many lines and status changes got their business.</returns>
    internal static Task<int> CompleteAsync(ApplicationDbContext context, CancellationToken cancellationToken)
        => context.Database.ExecuteSqlRawAsync(Sql, cancellationToken);
}
