using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Appointments.RequestNotices;

/// <summary>
/// Tells the live screens of the business that a request online arrived (Fase 29N, ADR 0068). It runs in-process right after
/// the commit and is best-effort, like the order events: the email is the durable notice (<see cref="AppointmentRequestedEmailsHandler"/>).
/// </summary>
public sealed class AppointmentRequestRealtimeNotifications : INotificationHandler<AppointmentRequestedEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly IAppointmentNotificationService _notifications;
    private readonly ILogger<AppointmentRequestRealtimeNotifications> _logger;

    public AppointmentRequestRealtimeNotifications(
        IApplicationDbContext context,
        IAppointmentNotificationService notifications,
        ILogger<AppointmentRequestRealtimeNotifications> logger)
    {
        _context = context;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task Handle(AppointmentRequestedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var request = await _context.Appointments
                .AsNoTracking()
                .Where(a => a.Id == notification.AppointmentId && a.TenantId == notification.TenantId)
                .Select(a => new { a.ServiceName, a.StartAt })
                .FirstOrDefaultAsync(cancellationToken);
            if (request is null)
                return;

            await _notifications.NotifyAppointmentRequested(
                notification.TenantId,
                new AppointmentRequestedNotice(
                    notification.AppointmentId, request.ServiceName, request.StartAt, notification.HomeVisit, notification.FromTeam));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Realtime notification failed for {EventType} {EventId}", nameof(AppointmentRequestedEvent), notification.EventId);
        }
    }
}
