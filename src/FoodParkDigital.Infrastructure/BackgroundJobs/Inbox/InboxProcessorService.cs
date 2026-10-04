using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.BackgroundJobs.Inbox;

/// <summary>Drives <see cref="InboxProcessor"/>: drains due deliveries, then waits for a new one or the next poll.</summary>
public sealed class InboxProcessorService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private readonly InboxProcessor _processor;
    private readonly InboxSignal _signal;
    private readonly BackgroundJobOptions.QueueOptions _options;
    private readonly ILogger<InboxProcessorService> _logger;

    public InboxProcessorService(
        InboxProcessor processor,
        InboxSignal signal,
        IOptions<BackgroundJobOptions> options,
        ILogger<InboxProcessorService> logger)
    {
        _processor = processor;
        _signal = signal;
        _options = options.Value.Inbox;
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
                while (await _processor.ProcessDueAsync(stoppingToken) > 0)
                {
                }

                if (DateTimeOffset.UtcNow >= nextCleanup)
                {
                    await _processor.DeleteProcessedAsync(stoppingToken);
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
                _logger.LogError(Services.LogRedaction.RedactDatabaseMessages(ex), "Webhook inbox pass failed");
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
