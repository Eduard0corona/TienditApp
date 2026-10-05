using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Application.Messaging.Campaigns;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Appointments;

/// <summary>
/// Queues the WhatsApp notice of an appointment that was booked, moved, confirmed or cancelled. The caller saves it
/// with the change (same SaveChanges, so there is no notice without the change nor the other way around) and the
/// messaging worker sends it with retries. Each notice says the whole appointment (service, business, day and
/// time), so when a newer one of the same appointment is waiting, the older one is skipped instead of arriving
/// after it. Internal steps (in service, completed, no-show) do not message the customer.
/// </summary>
public sealed class AppointmentNotices
{
    private readonly IApplicationDbContext _context;
    private readonly IPhoneNormalizationService _phones;
    private readonly IDateTimeProvider _clock;

    public AppointmentNotices(IApplicationDbContext context, IPhoneNormalizationService phones, IDateTimeProvider clock)
    {
        _context = context;
        _phones = phones;
        _clock = clock;
    }

    /// <returns>True when a notice was queued: wake the messaging worker after saving.</returns>
    public Task<bool> QueueScheduledAsync(Appointment appointment, CancellationToken cancellationToken)
        => QueueAsync(appointment, (service, business, date, time) =>
            $"quedó agendada tu cita de {service} el {date} a las {time} en {business}.", cancellationToken);

    /// <inheritdoc cref="QueueScheduledAsync"/>
    public Task<bool> QueueRescheduledAsync(Appointment appointment, CancellationToken cancellationToken)
        => QueueAsync(appointment, (service, business, date, time) =>
            $"tu cita de {service} ahora es el {date} a las {time} en {business}.", cancellationToken);

    /// <inheritdoc cref="QueueScheduledAsync"/>
    public Task<bool> QueueStatusChangedAsync(Appointment appointment, CancellationToken cancellationToken)
        => appointment.Status switch
        {
            AppointmentStatus.Confirmed => QueueAsync(appointment, (service, business, date, time) =>
                $"tu cita de {service} del {date} a las {time} en {business} está confirmada.", cancellationToken),
            AppointmentStatus.Cancelled => QueueAsync(appointment, (service, business, date, time) =>
                $"tu cita de {service} del {date} a las {time} en {business} fue cancelada. "
                + "Respóndenos si quieres agendar otra.", cancellationToken),
            _ => Task.FromResult(false)
        };

    // The time ("10:00 a. m.") never ends the sentence, or it would read "a. m..".
    private async Task<bool> QueueAsync(
        Appointment appointment,
        Func<string, string, string, string, string> describe,
        CancellationToken cancellationToken)
    {
        // Without WhatsApp, or with a number it cannot reach, there is nothing to queue (the agenda still works).
        if (!_phones.TryNormalizeToE164(appointment.CustomerPhone, "MX", out var phone)
            || !await CampaignRules.IsWhatsAppConnectedAsync(_context, appointment.TenantId, cancellationToken))
            return false;

        var timeZone = await _context.TenantSettings
            .AsNoTracking()
            .Where(s => s.TenantId == appointment.TenantId)
            .Select(s => s.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var businessName = await CampaignLauncher.BusinessNameAsync(_context, appointment.TenantId, cancellationToken);
        var local = MessageValues.ToLocal(appointment.StartAt, timeZone);
        var body = $"Hola {MessageValues.FirstName(appointment.CustomerName)}, "
            + describe(appointment.ServiceName, businessName, MessageValues.FormatDate(local), MessageValues.FormatTime(local));

        _context.OutgoingMessages.Add(new OutgoingMessage(
            appointment.TenantId,
            MessagePurpose.AppointmentNotice,
            phone,
            appointment.CustomerName,
            body,
            providerTemplateName: null,
            MessageTemplate.DefaultLanguageCode,
            parameters: [],
            notBefore: _clock.UtcNow,
            appointmentId: appointment.Id));
        return true;
    }
}
