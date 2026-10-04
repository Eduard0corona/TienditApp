using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.BackgroundJobs.Messaging;

/// <summary>
/// Drives <see cref="MessagingWorker"/>: starts and completes campaigns on every pass, looks for due reminders
/// every few minutes and drains the queue of messages, then waits for a new campaign or the next poll.
/// </summary>
public sealed class MessagingWorkerService : BackgroundService
{
    private readonly MessagingWorker _worker;
    private readonly MessagingSignal _signal;
    private readonly BackgroundJobOptions.MessagingOptions _options;
    private readonly ILogger<MessagingWorkerService> _logger;

    public MessagingWorkerService(
        MessagingWorker worker,
        MessagingSignal signal,
        IOptions<BackgroundJobOptions> options,
        ILogger<MessagingWorkerService> logger)
    {
        _worker = worker;
        _signal = signal;
        _options = options.Value.Messaging;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        var reminderInterval = TimeSpan.FromMinutes(Math.Max(1, _options.ReminderScanMinutes));
        var nextReminderScan = DateTimeOffset.UtcNow;
        var consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _worker.StartDueCampaignsAsync(stoppingToken);

                if (DateTimeOffset.UtcNow >= nextReminderScan)
                {
                    await _worker.QueueAppointmentRemindersAsync(stoppingToken);
                    nextReminderScan = DateTimeOffset.UtcNow.Add(reminderInterval);
                }

                while (await _worker.DispatchDueAsync(stoppingToken) > 0)
                {
                }

                await _worker.CompleteFinishedCampaignsAsync(stoppingToken);
                consecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                _logger.LogError(Services.LogRedaction.RedactDatabaseMessages(ex), "Messaging pass failed");
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
