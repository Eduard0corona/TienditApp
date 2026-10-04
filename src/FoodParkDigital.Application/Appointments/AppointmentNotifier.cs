using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Appointments;

/// <summary>
/// Best-effort WhatsApp notifications for appointments. The send is awaited inside the request
/// (it used to be fire-and-forget on the request's DbContext) and times are shown in the tenant's
/// time zone instead of UTC.
/// </summary>
public static class AppointmentNotifier
{
    public static async Task NotifyScheduledAsync(
        IApplicationDbContext context,
        IWhatsAppService whatsAppService,
        ILogger logger,
        Appointment appointment,
        CancellationToken cancellationToken)
    {
        var localStart = await ToTenantTimeAsync(context, appointment.TenantId, appointment.StartAt, cancellationToken);
        await SendAsync(
            whatsAppService,
            logger,
            appointment,
            $"Tu cita para {appointment.ServiceName} fue agendada para {localStart:dd/MM/yyyy HH:mm}.",
            cancellationToken);
    }

    public static async Task NotifyRescheduledAsync(
        IApplicationDbContext context,
        IWhatsAppService whatsAppService,
        ILogger logger,
        Appointment appointment,
        CancellationToken cancellationToken)
    {
        var localStart = await ToTenantTimeAsync(context, appointment.TenantId, appointment.StartAt, cancellationToken);
        await SendAsync(
            whatsAppService,
            logger,
            appointment,
            $"Tu cita para {appointment.ServiceName} cambió al {localStart:dd/MM/yyyy} a las {localStart:HH:mm}.",
            cancellationToken);
    }

    public static Task NotifyStatusChangedAsync(
        IWhatsAppService whatsAppService,
        ILogger logger,
        Appointment appointment,
        CancellationToken cancellationToken) =>
        SendAsync(
            whatsAppService,
            logger,
            appointment,
            $"Tu cita ({appointment.ServiceName}) ahora está: {ToSpanish(appointment.Status)}.",
            cancellationToken);

    public static async Task<DateTimeOffset> ToTenantTimeAsync(
        IApplicationDbContext context,
        Guid tenantId,
        DateTimeOffset utc,
        CancellationToken cancellationToken)
    {
        var timeZoneId = await context.TenantSettings
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);

        return TimeZoneInfo.ConvertTime(utc, TenantDateTimeResolver.Resolve(timeZoneId));
    }

    public static string ToSpanish(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Scheduled => "agendada",
        AppointmentStatus.Confirmed => "confirmada",
        AppointmentStatus.InService => "en servicio",
        AppointmentStatus.Completed => "completada",
        AppointmentStatus.NoShow => "marcada como no asistida",
        AppointmentStatus.Cancelled => "cancelada",
        _ => status.ToString()
    };

    private static async Task SendAsync(
        IWhatsAppService whatsAppService,
        ILogger logger,
        Appointment appointment,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await whatsAppService.SendTextMessageAsync(
                appointment.TenantId,
                appointment.CustomerPhone,
                message,
                cancellationToken);
            if (result.IsFailure)
                logger.LogWarning("Appointment notification not sent for {AppointmentId}: {Error}", appointment.Id, result.Error);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The appointment is already saved; a notification failure must not fail the request.
            logger.LogWarning(ex, "Appointment notification failed for {AppointmentId}.", appointment.Id);
        }
    }
}
