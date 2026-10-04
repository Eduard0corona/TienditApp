using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.BackgroundJobs.Inbox;

/// <summary>
/// Processes stored webhook deliveries. A delivery with a message that could not be stored is retried as a
/// whole: messages already stored are recognized by their provider id and skipped.
/// </summary>
public sealed class InboxProcessor
{
    private const string WebhookActor = "whatsapp-webhook";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly BackgroundJobOptions.QueueOptions _options;
    private readonly ILogger<InboxProcessor> _logger;

    public InboxProcessor(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider clock,
        IOptions<BackgroundJobOptions> options,
        ILogger<InboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _options = options.Value.Inbox;
        _logger = logger;
    }

    /// <returns>How many deliveries were claimed; zero when nothing was due.</returns>
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken)
    {
        var claimed = await ClaimDueAsync(cancellationToken);
        foreach (var message in claimed)
            await ProcessAsync(message, cancellationToken);

        return claimed.Count;
    }

    public async Task<int> DeleteProcessedAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cutoff = _clock.UtcNow.AddDays(-_options.RetentionDays);

        var expired = await context.InboxMessages
            .Where(m => (m.ProcessedAt != null && m.ProcessedAt < cutoff)
                || (m.DeadLetteredAt != null && m.DeadLetteredAt < cutoff))
            .OrderBy(m => m.CreatedAt)
            .Take(500)
            .ToListAsync(cancellationToken);
        if (expired.Count == 0)
            return 0;

        context.InboxMessages.RemoveRange(expired);
        await context.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    private async Task<List<InboxMessage>> ClaimDueAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = _clock.UtcNow;

        var candidates = await context.InboxMessages
            .Where(m => m.ProcessedAt == null
                && m.DeadLetteredAt == null
                && m.NextAttemptAt <= now
                && (m.LockedUntil == null || m.LockedUntil < now))
            .OrderBy(m => m.CreatedAt)
            .Take(Math.Max(1, _options.BatchSize))
            .ToListAsync(cancellationToken);

        var claimed = new List<InboxMessage>(candidates.Count);
        foreach (var message in candidates)
        {
            message.Claim(now, TimeSpan.FromSeconds(_options.LeaseSeconds));
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                claimed.Add(message);
            }
            catch (DbUpdateConcurrencyException)
            {
                context.Entry(message).State = EntityState.Detached;
            }
        }

        return claimed;
    }

    private async Task ProcessAsync(InboxMessage message, CancellationToken cancellationToken)
    {
        string? failure;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<IBackgroundExecutionContext>().SetActor(null, WebhookActor);
            var outcome = message.Source switch
            {
                WebhookSources.WhatsApp => await services.GetRequiredService<WhatsAppWebhookProcessor>()
                    .ProcessAsync(message.Payload, cancellationToken),
                _ => throw new InvalidOperationException($"Unknown webhook source '{message.Source}'.")
            };
            failure = outcome.FailedItems > 0
                ? $"{outcome.FailedItems} message(s) or status batches could not be stored."
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                LogRedaction.RedactDatabaseMessages(ex),
                "Webhook delivery {InboxMessageId} could not be processed",
                message.Id);
            failure = FailureDescription.Describe(ex);
        }

        await using var completionScope = _scopeFactory.CreateAsyncScope();
        var context = completionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Attach(message);
        var now = _clock.UtcNow;
        if (failure is null)
        {
            message.MarkProcessed(now);
        }
        else if (message.MarkFailed(now, failure, _options.MaxAttempts))
        {
            _logger.LogError(
                "Webhook delivery {InboxMessageId} was set aside after {Attempts} attempts: {Failure}",
                message.Id,
                message.Attempts,
                failure);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Webhook delivery {InboxMessageId} was claimed by another worker before it finished", message.Id);
        }
    }
}
