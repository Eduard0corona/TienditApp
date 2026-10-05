using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Messaging;

public enum OutgoingSendDecision
{
    Sent = 0,
    /// <summary>Not sent on purpose: opt-out, or the appointment changed.</summary>
    Skipped = 1,
    /// <summary>Its campaign was cancelled.</summary>
    Cancelled = 2,
    /// <summary>May be accepted later: the worker retries it.</summary>
    TransientFailure = 3,
    Rejected = 4
}

public sealed record OutgoingSendOutcome(OutgoingSendDecision Decision, string? ProviderMessageId = null, string? Reason = null);

/// <summary>
/// Sends one queued message in its business's context, checking first that it still applies: the customer has
/// not opted out (campaigns and reminders), the campaign was not cancelled, the appointment did not change and no
/// newer notice of the same appointment is waiting. The worker records the outcome and retries transient failures.
/// </summary>
public sealed class OutgoingMessageSender
{
    public const string OptedOutReason = "El cliente pidió no recibir mensajes.";
    public const string AppointmentChangedReason = "La cita cambió, se canceló o ya pasó.";
    public const string NoticeSupersededReason = "Hay un aviso más reciente de la misma cita.";
    public const string NoticeExpiredReason = "El aviso esperó demasiado para enviarse.";

    /// <summary>A notice that could not go out in this time (the worker was stopped) is no longer news.</summary>
    public static readonly TimeSpan NoticeLifetime = TimeSpan.FromHours(24);

    private readonly IApplicationDbContext _context;
    private readonly IWhatsAppService _whatsApp;
    private readonly MessagingConsentService _consent;
    private readonly IDateTimeProvider _clock;

    public OutgoingMessageSender(
        IApplicationDbContext context,
        IWhatsAppService whatsApp,
        MessagingConsentService consent,
        IDateTimeProvider clock)
    {
        _context = context;
        _whatsApp = whatsApp;
        _consent = consent;
        _clock = clock;
    }

    public async Task<OutgoingSendOutcome> SendAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        if (message.CampaignId is { } campaignId)
        {
            var status = await _context.MessageCampaigns
                .AsNoTracking()
                .Where(c => c.Id == campaignId)
                .Select(c => (CampaignStatus?)c.Status)
                .FirstOrDefaultAsync(cancellationToken);
            if (status is null or CampaignStatus.Cancelled)
                return new OutgoingSendOutcome(OutgoingSendDecision.Cancelled);
        }

        if (message.Purpose == MessagePurpose.AppointmentReminder)
        {
            var appointment = await _context.Appointments
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == message.AppointmentId, cancellationToken);
            if (appointment is null
                || appointment.Status is not (AppointmentStatus.Scheduled or AppointmentStatus.Confirmed)
                || appointment.StartAt <= _clock.UtcNow
                || AppointmentReminderPlanner.Key(appointment) != message.DeduplicationKey)
            {
                return new OutgoingSendOutcome(OutgoingSendDecision.Skipped, Reason: AppointmentChangedReason);
            }
        }

        if (message.Purpose == MessagePurpose.AppointmentNotice)
        {
            if (message.CreatedAt < _clock.UtcNow - NoticeLifetime)
                return new OutgoingSendOutcome(OutgoingSendDecision.Skipped, Reason: NoticeExpiredReason);
            if (message.AppointmentId is null)
                return new OutgoingSendOutcome(OutgoingSendDecision.Skipped, Reason: AppointmentChangedReason);

            // Each notice tells the whole appointment: a newer one makes this one stale (a retry must not arrive last).
            var superseded = await _context.OutgoingMessages
                .AsNoTracking()
                .AnyAsync(m => m.AppointmentId == message.AppointmentId
                    && m.Purpose == MessagePurpose.AppointmentNotice
                    && m.CreatedAt > message.CreatedAt, cancellationToken);
            if (superseded)
                return new OutgoingSendOutcome(OutgoingSendDecision.Skipped, Reason: NoticeSupersededReason);
        }

        // An opt-out stops promotions and reminders; notices of the customer's own orders and appointments still go out.
        if (message.Purpose != MessagePurpose.AppointmentNotice
            && await _consent.IsOptedOutAsync(message.TenantId, message.Phone, cancellationToken))
            return new OutgoingSendOutcome(OutgoingSendDecision.Skipped, Reason: OptedOutReason);

        var result = message.ProviderTemplateName is { } templateName
            ? await _whatsApp.SendTemplateMessageAsync(
                message.TenantId,
                message.Phone,
                templateName,
                message.LanguageCode,
                message.GetParameters().ToArray(),
                cancellationToken)
            : await _whatsApp.SendTextMessageAsync(message.TenantId, message.Phone, message.Body, cancellationToken);

        if (result.IsSuccess)
            return new OutgoingSendOutcome(OutgoingSendDecision.Sent, result.ProviderMessageId);

        return new OutgoingSendOutcome(
            result.IsTransient ? OutgoingSendDecision.TransientFailure : OutgoingSendDecision.Rejected,
            Reason: result.Error);
    }
}
