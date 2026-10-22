using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.BackgroundJobs.Messaging;

/// <summary>
/// Starts due campaigns, queues appointment reminders and sends the queued WhatsApp messages with retries.
/// It finds work across businesses (an explicit system scope, the only place it reads without the tenant
/// filter) and does each piece in a scope of its own business, where the Application services run with the
/// query filters on.
/// </summary>
public sealed class MessagingWorker
{
    private const string WorkerActor = "mensajes";
    private const int CampaignsPerPass = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly BackgroundJobOptions.MessagingOptions _options;
    private readonly ILogger<MessagingWorker> _logger;

    public MessagingWorker(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider clock,
        IOptions<BackgroundJobOptions> options,
        ILogger<MessagingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _options = options.Value.Messaging;
        _logger = logger;
    }

    /// <returns>Campaigns started.</returns>
    public async Task<int> StartDueCampaignsAsync(CancellationToken cancellationToken)
    {
        List<(Guid Id, Guid TenantId)> due;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = _clock.UtcNow;
            due = (await context.MessageCampaigns
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    // A business suspended for billing does not send campaigns; they start once it is active again.
                    .Where(c => c.Status == CampaignStatus.Scheduled && c.ScheduledAt <= now
                        && context.Tenants.Any(t => t.Id == c.TenantId && t.Status == TenantStatus.Active))
                    .OrderBy(c => c.ScheduledAt)
                    .Take(CampaignsPerPass)
                    .Select(c => new { c.Id, c.TenantId })
                    .ToListAsync(cancellationToken))
                .Select(c => (c.Id, c.TenantId))
                .ToList();
        }

        var started = 0;
        foreach (var (campaignId, tenantId) in due)
        {
            try
            {
                await using var scope = _scopeFactory.CreateTenantScope(tenantId, WorkerActor);
                var services = scope.ServiceProvider;
                var context = services.GetRequiredService<ApplicationDbContext>();
                var campaign = await context.MessageCampaigns.FirstOrDefaultAsync(c => c.Id == campaignId, cancellationToken);
                if (campaign is not { Status: CampaignStatus.Scheduled })
                    continue;

                var queued = await services.GetRequiredService<CampaignLauncher>().LaunchAsync(campaign, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
                started++;
                _logger.LogInformation(
                    "Campaign {CampaignId} of tenant {TenantId} started with {Recipients} messages",
                    campaignId,
                    tenantId,
                    queued);
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogInformation("Campaign {CampaignId} was started or cancelled by someone else first", campaignId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Nothing was saved: the campaign stays scheduled and the next pass tries again.
                _logger.LogError(LogRedaction.RedactDatabaseMessages(ex), "Campaign {CampaignId} could not be started", campaignId);
            }
        }

        return started;
    }

    /// <returns>Campaigns marked as completed.</returns>
    public async Task<int> CompleteFinishedCampaignsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var finished = await context.MessageCampaigns
            .IgnoreQueryFilters()
            .Where(c => c.Status == CampaignStatus.Sending
                && !context.OutgoingMessages.IgnoreQueryFilters()
                    .Any(m => m.CampaignId == c.Id && m.Status == OutgoingMessageStatus.Pending))
            .Take(CampaignsPerPass)
            .ToListAsync(cancellationToken);

        var completed = 0;
        foreach (var campaign in finished)
        {
            campaign.Complete(_clock.UtcNow);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                completed++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Cancelled at the same moment: the cancellation stands.
                context.Entry(campaign).State = EntityState.Detached;
            }
        }

        return completed;
    }

    /// <returns>Reminders queued.</returns>
    public async Task<int> QueueAppointmentRemindersAsync(CancellationToken cancellationToken)
    {
        List<Guid> tenants;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // Which businesses have reminders on, before entering each one's scope below.
            tenants = await context.TenantSettings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s => s.AppointmentRemindersEnabled
                    && (s.Capabilities & TenantCapability.Appointments) == TenantCapability.Appointments
                    && context.Tenants.Any(t => t.Id == s.TenantId && t.Status == TenantStatus.Active))
                .Select(s => s.TenantId)
                .ToListAsync(cancellationToken);
        }

        var queued = 0;
        foreach (var tenantId in tenants)
        {
            try
            {
                await using var scope = _scopeFactory.CreateTenantScope(tenantId, WorkerActor);
                var services = scope.ServiceProvider;
                var context = services.GetRequiredService<ApplicationDbContext>();
                var settings = await context.TenantSettings.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
                if (settings is null)
                    continue;

                var count = await services.GetRequiredService<AppointmentReminderPlanner>().QueueDueAsync(settings, cancellationToken);
                if (count == 0)
                    continue;

                await context.SaveChangesAsync(cancellationToken);
                queued += count;
            }
            catch (DbUpdateException ex)
            {
                // Another instance queued some of them at the same time (unique key); the next pass adds the rest.
                _logger.LogInformation(
                    LogRedaction.RedactDatabaseMessages(ex),
                    "Appointment reminders of tenant {TenantId} were not queued in this pass",
                    tenantId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    LogRedaction.RedactDatabaseMessages(ex),
                    "Appointment reminders of tenant {TenantId} could not be queued",
                    tenantId);
            }
        }

        return queued;
    }

    /// <summary>
    /// Queues the reminders of what customers owe past their term (Fase 26C) and the notices of what comes due soon (Fase 26E)
    /// in each business that turned them on; the planners decide whether it is the business's hour and who is due. Each runs
    /// in its own scope and saves on its own, so a collision in one does not drop the other.
    /// </summary>
    /// <returns>Reminders and notices queued.</returns>
    public async Task<int> QueueCreditRemindersAsync(CancellationToken cancellationToken)
    {
        List<(Guid TenantId, bool Reminders, bool DueSoon)> tenants;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // Which businesses have credit reminders or notices on, before entering each one's scope below.
            tenants = (await context.TenantSettings
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(s => (s.CreditRemindersEnabled || s.CreditDueSoonEnabled)
                        && context.Tenants.Any(t => t.Id == s.TenantId && t.Status == TenantStatus.Active))
                    .Select(s => new { s.TenantId, s.CreditRemindersEnabled, s.CreditDueSoonEnabled })
                    .ToListAsync(cancellationToken))
                .Select(s => (s.TenantId, s.CreditRemindersEnabled, s.CreditDueSoonEnabled))
                .ToList();
        }

        var queued = 0;
        foreach (var (tenantId, reminders, dueSoon) in tenants)
        {
            if (reminders)
                queued += await QueueInTenantAsync(tenantId, "Credit reminders",
                    (services, settings, ct) => services.GetRequiredService<CreditReminderPlanner>().QueueDueAsync(settings, ct),
                    cancellationToken);
            if (dueSoon)
                queued += await QueueInTenantAsync(tenantId, "Credit due-soon notices",
                    (services, settings, ct) => services.GetRequiredService<CreditDueSoonPlanner>().QueueDueAsync(settings, ct),
                    cancellationToken);
        }

        return queued;
    }

    /// <summary>Runs one planner in the business's scope and saves what it queued.</summary>
    private async Task<int> QueueInTenantAsync(
        Guid tenantId,
        string what,
        Func<IServiceProvider, TenantSettings, CancellationToken, Task<int>> queue,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateTenantScope(tenantId, WorkerActor);
            var services = scope.ServiceProvider;
            var context = services.GetRequiredService<ApplicationDbContext>();
            var settings = await context.TenantSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
            if (settings is null)
                return 0;

            var count = await queue(services, settings, cancellationToken);
            if (count == 0)
                return 0;

            await context.SaveChangesAsync(cancellationToken);
            return count;
        }
        catch (DbUpdateException ex)
        {
            // Another instance queued some of them at the same time (unique key); the next pass adds the rest.
            _logger.LogInformation(
                LogRedaction.RedactDatabaseMessages(ex),
                "{What} of tenant {TenantId} were not queued in this pass",
                what,
                tenantId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                LogRedaction.RedactDatabaseMessages(ex),
                "{What} of tenant {TenantId} could not be queued",
                what,
                tenantId);
        }

        return 0;
    }

    /// <returns>Messages claimed; zero when nothing was due.</returns>
    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken)
    {
        var claimed = await ClaimDueAsync(cancellationToken);
        foreach (var message in claimed)
        {
            var sent = await ProcessAsync(message, cancellationToken);
            if (sent && _options.SendIntervalMilliseconds > 0)
                await Task.Delay(_options.SendIntervalMilliseconds, cancellationToken);
        }

        return claimed.Count;
    }

    private async Task<List<OutgoingMessage>> ClaimDueAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = _clock.UtcNow;

        var candidates = await context.OutgoingMessages
            .IgnoreQueryFilters()
            .Where(m => m.Status == OutgoingMessageStatus.Pending
                && m.NextAttemptAt <= now
                && (m.LockedUntil == null || m.LockedUntil < now))
            .OrderBy(m => m.NextAttemptAt)
            .ThenBy(m => m.CreatedAt)
            .Take(Math.Max(1, _options.BatchSize))
            .ToListAsync(cancellationToken);

        var claimed = new List<OutgoingMessage>(candidates.Count);
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

    /// <returns>True when the message was handed to the provider.</returns>
    private async Task<bool> ProcessAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        OutgoingSendOutcome outcome;
        try
        {
            await using var scope = _scopeFactory.CreateTenantScope(message.TenantId, WorkerActor);
            outcome = await scope.ServiceProvider.GetRequiredService<OutgoingMessageSender>().SendAsync(message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(LogRedaction.RedactDatabaseMessages(ex), "Outgoing message {MessageId} could not be processed", message.Id);
            outcome = new OutgoingSendOutcome(OutgoingSendDecision.TransientFailure, Reason: FailureDescription.Describe(ex));
        }

        await using var completionScope = _scopeFactory.CreateAsyncScope();
        var context = completionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        context.Attach(message);
        var now = _clock.UtcNow;
        switch (outcome.Decision)
        {
            case OutgoingSendDecision.Sent:
                message.MarkSent(now, outcome.ProviderMessageId);
                break;
            case OutgoingSendDecision.Skipped:
                message.Skip(outcome.Reason ?? "No se envió.");
                break;
            case OutgoingSendDecision.Cancelled:
                message.Cancel();
                break;
            case OutgoingSendDecision.Rejected:
                message.Fail(outcome.Reason ?? "WhatsApp rechazó el mensaje.");
                break;
            default:
                var reason = outcome.Reason ?? "Error temporal al enviar.";
                if (message.Attempts + 1 >= _options.MaxAttempts)
                {
                    message.Fail(reason);
                    _logger.LogWarning("Outgoing message {MessageId} failed after {Attempts} attempts", message.Id, message.Attempts);
                }
                else
                {
                    message.RetryAt(now.Add(RetrySchedule.DelayAfter(message.Attempts + 1)), reason);
                }

                break;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Outgoing message {MessageId} was claimed by another worker before it finished", message.Id);
        }

        return outcome.Decision == OutgoingSendDecision.Sent;
    }
}
