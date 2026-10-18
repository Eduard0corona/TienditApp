using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Messaging;
using FoodParkDigital.Application.Messaging.Campaigns;
using FoodParkDigital.Application.Orders;
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
/// after it. Internal steps (in service, completed, no-show) do not message the customer. An appointment at the customer's
/// address (Fase 22E) says so with its postal code, never the whole address (the text stays in the messages' log), and the
/// charge for the visit.
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
        => QueueAsync(appointment, (service, place, date, time) =>
            $"quedó agendada tu cita de {service} el {date} a las {time} {place}.", withVisitFee: true, cancellationToken);

    /// <summary>
    /// A customer booked online (Fase 24D): the business still has to accept it. When it does, the notice of an appointment
    /// agendada follows (<see cref="QueueScheduledAsync"/>).
    /// </summary>
    /// <inheritdoc cref="QueueScheduledAsync"/>
    public Task<bool> QueueRequestedAsync(Appointment appointment, CancellationToken cancellationToken)
        => QueueAsync(appointment, (service, place, date, time) =>
            $"recibimos tu solicitud de cita de {service} el {date} a las {time} {place}. Te avisamos en cuanto la confirmen.",
            withVisitFee: false, cancellationToken);

    /// <summary>The business declined a request made online (Fase 24D): its time is free again.</summary>
    /// <inheritdoc cref="QueueScheduledAsync"/>
    public Task<bool> QueueDeclinedAsync(Appointment appointment, CancellationToken cancellationToken)
        => QueueAsync(appointment, (service, place, date, time) =>
            $"no pudimos confirmar tu cita de {service} del {date} a las {time} {place}. "
            + "Elige otro horario en su menú en línea o respóndenos.", withVisitFee: false, cancellationToken);

    /// <summary>
    /// The appointment moved to another time or place. A request made online that the business moves before accepting it
    /// (Fase 24D) is still waiting: the notice says so, and the one of an appointment agendada follows when it accepts.
    /// </summary>
    /// <inheritdoc cref="QueueScheduledAsync"/>
    public Task<bool> QueueRescheduledAsync(Appointment appointment, CancellationToken cancellationToken)
        => appointment.IsRequest
            ? QueueAsync(appointment, (service, place, date, time) =>
                $"tu solicitud de cita de {service} ahora es para el {date} a las {time} {place}. Te avisamos en cuanto la confirmen.",
                withVisitFee: false, cancellationToken)
            : QueueAsync(appointment, (service, place, date, time) =>
                $"tu cita de {service} ahora es el {date} a las {time} {place}.", withVisitFee: true, cancellationToken);

    /// <inheritdoc cref="QueueScheduledAsync"/>
    public Task<bool> QueueStatusChangedAsync(Appointment appointment, CancellationToken cancellationToken)
        => appointment.Status switch
        {
            AppointmentStatus.Confirmed => QueueAsync(appointment, (service, place, date, time) =>
                $"tu cita de {service} del {date} a las {time} {place} está confirmada.", withVisitFee: true, cancellationToken),
            AppointmentStatus.Cancelled => QueueAsync(appointment, (service, place, date, time) =>
                $"tu cita de {service} del {date} a las {time} {place} fue cancelada. "
                + "Respóndenos si quieres agendar otra.", withVisitFee: false, cancellationToken),
            _ => Task.FromResult(false)
        };

    // The time ("10:00 a. m.") never ends the sentence, or it would read "a. m..". The place is «en Barbería Luis» at the
    // business and «a domicilio (CP 72000) con Barbería Luis» at the customer's address.
    private async Task<bool> QueueAsync(
        Appointment appointment,
        Func<string, string, string, string, string> describe,
        bool withVisitFee,
        CancellationToken cancellationToken)
    {
        // Without WhatsApp, or with a number it cannot reach, there is nothing to queue (the agenda still works).
        if (!_phones.TryNormalizeToE164(appointment.CustomerPhone, "MX", out var phone)
            || !await CampaignRules.IsWhatsAppConnectedAsync(_context, appointment.TenantId, cancellationToken))
            return false;

        var settings = await _context.TenantSettings
            .AsNoTracking()
            .Where(s => s.TenantId == appointment.TenantId)
            .Select(s => new { s.TimeZone, s.PricesIncludeTax })
            .FirstOrDefaultAsync(cancellationToken);
        var businessName = await CampaignLauncher.BusinessNameAsync(_context, appointment.TenantId, cancellationToken);
        var local = MessageValues.ToLocal(appointment.StartAt, settings?.TimeZone);
        var place = appointment.Visit is { } visit
            ? $"a domicilio (CP {visit.PostalCode}) con {businessName}"
            : $"en {businessName}";
        var body = $"Hola {MessageValues.FirstName(appointment.CustomerName)}, "
            + describe(appointment.ServiceName, place, MessageValues.FormatDate(local), MessageValues.FormatTime(local));
        // The visit charges like the business's prices: with IVA added when they do not include it (ADR 0018).
        if (withVisitFee && appointment.Visit is { Fee: > 0 } charged)
        {
            body += $" Cargo por visita: {OrderStatusLabels.FormatMoney(charged.Fee)}"
                + (settings?.PricesIncludeTax == false ? " más IVA." : ".");
        }

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
