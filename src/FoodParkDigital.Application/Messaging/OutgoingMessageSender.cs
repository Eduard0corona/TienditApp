using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Messaging;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Messaging;

public enum OutgoingSendDecision
{
    Sent = 0,
    /// <summary>Not sent on purpose: opt-out, the appointment changed, or nothing is overdue or comes due that day anymore.</summary>
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
/// not opted out (campaigns and reminders), the campaign was not cancelled, the appointment did not change, no
/// newer notice of the same appointment is waiting, the customer still owes something overdue (Fase 26C), and still owes
/// what comes due on the day of the notice before it does (Fase 26E). The worker records the outcome and retries transient
/// failures.
/// </summary>
public sealed class OutgoingMessageSender
{
    public const string OptedOutReason = "El cliente pidió no recibir mensajes.";
    public const string AppointmentChangedReason = "La cita cambió, se canceló o ya pasó.";
    public const string NoticeSupersededReason = "Hay un aviso más reciente de la misma cita.";
    public const string NoticeExpiredReason = "El aviso esperó demasiado para enviarse.";
    public const string NothingOverdueReason = "Ya no tiene nada vencido.";
    public const string CreditRemindersOffReason = "Los recordatorios del fiado se apagaron.";
    public const string CreditReminderLateReason = "El recordatorio ya no se mandó: se pasó la hora de ese día.";
    public const string NothingDueSoonReason = "Ya no debe nada que venza ese día.";
    public const string OverdueInsteadReason = "Ya tiene algo vencido: le toca el recordatorio de lo vencido.";
    public const string CreditDueSoonOffReason = "El aviso antes de vencer se apagó.";
    public const string CreditDueSoonLateReason = "El aviso ya no se mandó: se pasó la hora de ese día.";

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

        if (message.Purpose == MessagePurpose.CreditReminder
            && await CreditReminderSkipReasonAsync(message, cancellationToken) is { } skipped)
            return new OutgoingSendOutcome(OutgoingSendDecision.Skipped, Reason: skipped);

        if (message.Purpose == MessagePurpose.CreditDueSoon
            && await CreditDueSoonSkipReasonAsync(message, cancellationToken) is { } notDue)
            return new OutgoingSendOutcome(OutgoingSendDecision.Skipped, Reason: notDue);

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

    /// <summary>
    /// A credit reminder (Fase 26C) goes out only the business day it was queued, before night, while the reminders are on
    /// and the customer still owes something overdue: a payment or a write-off since it was queued makes it stale.
    /// </summary>
    private async Task<string?> CreditReminderSkipReasonAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        var settings = await _context.TenantSettings
            .AsNoTracking()
            .Select(s => new { s.CreditRemindersEnabled, s.CreditTermDays, s.TimeZone })
            .FirstOrDefaultAsync(cancellationToken);
        if (settings is not { CreditRemindersEnabled: true })
            return CreditRemindersOffReason;

        var now = _clock.UtcNow;
        if (IsLate(message, settings.TimeZone, now))
            return CreditReminderLateReason;

        var business = new CreditTerm.Settings(settings.CreditTermDays, settings.TimeZone);
        return await CreditTerm.HasOverdueAsync(_context, MessagingPhone.ToKey(message.Phone), business, now, cancellationToken)
            ? null
            : NothingOverdueReason;
    }

    /// <summary>
    /// A notice before the credit comes due (Fase 26E) goes out only the business day it was queued, before night, while the
    /// notice is on, the customer owes nothing overdue (a shorter term since it was queued makes it the overdue reminder's)
    /// and still owes something whose last day to pay, by their term now, is the notice's: a payment, a write-off or a new
    /// term since it was queued makes it stale.
    /// </summary>
    private async Task<string?> CreditDueSoonSkipReasonAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        var settings = await _context.TenantSettings
            .AsNoTracking()
            .Select(s => new { s.CreditDueSoonEnabled, s.CreditTermDays, s.TimeZone })
            .FirstOrDefaultAsync(cancellationToken);
        if (settings is not { CreditDueSoonEnabled: true })
            return CreditDueSoonOffReason;
        if (IsLate(message, settings.TimeZone, _clock.UtcNow))
            return CreditDueSoonLateReason;
        if (!CreditDueSoonPlanner.TryParseKey(message.DeduplicationKey, out _, out var dueOn, out _))
            return NothingDueSoonReason;

        var business = new CreditTerm.Settings(settings.CreditTermDays, settings.TimeZone);
        var phoneKey = MessagingPhone.ToKey(message.Phone);
        if (await CreditTerm.HasOverdueAsync(_context, phoneKey, business, _clock.UtcNow, cancellationToken))
            return OverdueInsteadReason;
        return await CreditTerm.OwesDueOnAsync(_context, phoneKey, business, dueOn, cancellationToken)
            ? null
            : NothingDueSoonReason;
    }

    /// <summary>A message about the fiado still waiting another business day, or at night, is no longer sent.</summary>
    private static bool IsLate(OutgoingMessage message, string? timeZone, DateTimeOffset now)
    {
        var zone = TenantDateTimeResolver.Resolve(timeZone);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        return local.Date != TimeZoneInfo.ConvertTime(message.CreatedAt, zone).Date
            || TimeOnly.FromDateTime(local.DateTime) >= CreditReminderPlanner.SendUntil;
    }
}
