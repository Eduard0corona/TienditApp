using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Appointments.RequestNotices;

/// <summary>
/// Tells the live screens of the business that a customer cancelled their appointment (Fase 29O, ADR 0069). It runs
/// in-process right after the commit and is best-effort, like the requests that arrive: the email is the durable notice
/// (<see cref="AppointmentCancelledEmailsHandler"/>).
/// </summary>
public sealed class AppointmentCancellationRealtimeNotifications : INotificationHandler<AppointmentCancelledByCustomerEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly IAppointmentNotificationService _notifications;
    private readonly ILogger<AppointmentCancellationRealtimeNotifications> _logger;

    public AppointmentCancellationRealtimeNotifications(
        IApplicationDbContext context,
        IAppointmentNotificationService notifications,
        ILogger<AppointmentCancellationRealtimeNotifications> logger)
    {
        _context = context;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task Handle(AppointmentCancelledByCustomerEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var appointment = await _context.Appointments
                .AsNoTracking()
                .Where(a => a.Id == notification.AppointmentId && a.TenantId == notification.TenantId)
                .Select(a => new { a.ServiceName, a.StartAt })
                .FirstOrDefaultAsync(cancellationToken);
            if (appointment is null)
                return;

            await _notifications.NotifyAppointmentCancelled(
                notification.TenantId,
                new AppointmentCancelledNotice(
                    notification.AppointmentId,
                    appointment.ServiceName,
                    appointment.StartAt,
                    notification.HomeVisit,
                    notification.FromTeam,
                    notification.WasRequest));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Realtime notification failed for {EventType} {EventId}", nameof(AppointmentCancelledByCustomerEvent), notification.EventId);
        }
    }
}
