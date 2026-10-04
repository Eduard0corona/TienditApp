using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.BackgroundJobs.Outbox;

/// <summary>Drives <see cref="OutboxDispatcher"/>: drains due messages, then waits for a commit or the next poll.</summary>
public sealed class OutboxDispatcherService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private readonly OutboxDispatcher _dispatcher;
    private readonly OutboxSignal _signal;
    private readonly BackgroundJobOptions.QueueOptions _options;
    private readonly ILogger<OutboxDispatcherService> _logger;

    public OutboxDispatcherService(
        OutboxDispatcher dispatcher,
        OutboxSignal signal,
        IOptions<BackgroundJobOptions> options,
        ILogger<OutboxDispatcherService> logger)
    {
        _dispatcher = dispatcher;
        _signal = signal;
        _options = options.Value.Outbox;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        var nextCleanup = DateTimeOffset.UtcNow;
        var consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await _dispatcher.DispatchDueAsync(stoppingToken) > 0)
                {
                }

                if (DateTimeOffset.UtcNow >= nextCleanup)
                {
                    await _dispatcher.DeleteProcessedAsync(stoppingToken);
                    nextCleanup = DateTimeOffset.UtcNow.Add(CleanupInterval);
                }

                consecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                // Typically the database being unreachable: wait for the next poll and try again.
                _logger.LogError(Services.LogRedaction.RedactDatabaseMessages(ex), "Outbox dispatch pass failed");
            }

            try
            {
                await _signal.WaitAsync(BackOff.Delay(pollInterval, consecutiveFailures), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
