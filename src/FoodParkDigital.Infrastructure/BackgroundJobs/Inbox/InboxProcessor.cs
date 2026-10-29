using FoodParkDigital.Application.Common.Diagnostics;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Infrastructure.OnlinePayments;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.BackgroundJobs.Inbox;

/// <summary>
/// Processes stored webhook deliveries. A delivery with a message that could not be stored is retried as a
/// whole: messages already stored are recognized by their provider id and skipped. The lease of each claimed delivery is
/// renewed while the batch is worked on (<see cref="LeaseKeeper{TItem}"/>, Fase 28K).
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
        await using var leases = new LeaseKeeper<InboxMessage>(
            _scopeFactory, _clock, TimeSpan.FromSeconds(_options.LeaseSeconds), claimed,
            (message, now, lease) => message.RenewLease(now, lease), message => message.Id, _logger, cancellationToken);
        foreach (var message in claimed)
            await ProcessAsync(message, leases, cancellationToken);

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

    private static string? Describe(WhatsAppWebhookOutcome outcome)
        => outcome.FailedItems > 0 ? $"{outcome.FailedItems} message(s) or status batches could not be stored." : null;

    private async Task ProcessAsync(InboxMessage message, LeaseKeeper<InboxMessage> leases, CancellationToken cancellationToken)
    {
        string? failure;
        var itemToken = leases.TokenFor(message);
        try
        {
            // No business yet: the processor finds it from the delivery and works in that business's scope.
            await using var scope = _scopeFactory.CreateSystemScope(WebhookActor);
            var services = scope.ServiceProvider;
            failure = message.Source switch
            {
                WebhookSources.WhatsApp => Describe(await services.GetRequiredService<WhatsAppWebhookProcessor>()
                    .ProcessAsync(message.Payload, itemToken)),
                WebhookSources.MercadoPago => await services.GetRequiredService<MercadoPagoWebhookProcessor>()
                    .ProcessAsync(message.Payload, itemToken),
                _ => throw new InvalidOperationException($"Unknown webhook source '{message.Source}'.")
            };
        }
        catch (OperationCanceledException) when (leases.IsLost(message) && !cancellationToken.IsCancellationRequested)
        {
            // Another instance holds it now and processes it again: what was stored here is recognized and skipped.
            return;
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

        if (!await leases.CompleteAsync(message, () => CompleteAsync(message, failure, cancellationToken), cancellationToken))
            _logger.LogWarning("Webhook delivery {InboxMessageId} was claimed by another worker before it finished", message.Id);
    }

    private async Task CompleteAsync(InboxMessage message, string? failure, CancellationToken cancellationToken)
    {
        await using var completionScope = _scopeFactory.CreateAsyncScope();
        var context = completionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Attach(message);
        var now = _clock.UtcNow;
        if (failure is null)
        {
            message.MarkProcessed(now);
            TienditAppMetrics.RecordInboxDelivery(MetricOutcomes.Processed, message.Source);
        }
        else if (message.MarkFailed(now, failure, _options.MaxAttempts))
        {
            TienditAppMetrics.RecordInboxDelivery(MetricOutcomes.DeadLettered, message.Source);
            _logger.LogError(
                "Webhook delivery {InboxMessageId} was set aside after {Attempts} attempts: {Failure}",
                message.Id,
                message.Attempts,
                failure);
        }
        else
        {
            TienditAppMetrics.RecordInboxDelivery(MetricOutcomes.Failed, message.Source);
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
