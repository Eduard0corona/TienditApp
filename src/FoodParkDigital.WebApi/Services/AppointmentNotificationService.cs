using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace FoodParkDigital.WebApi.Services;

/// <summary>
/// The requests online of a business, and since the Fase 29O the appointments its customers cancel, to the live screens of
/// whoever answers them (<see cref="AppointmentHub"/>).
/// </summary>
public class AppointmentNotificationService : IAppointmentNotificationService
{
    private readonly IHubContext<AppointmentHub> _hubContext;

    public AppointmentNotificationService(IHubContext<AppointmentHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyAppointmentRequested(Guid tenantId, AppointmentRequestedNotice notice)
    {
        // What was asked for and when; who asked stays in the agenda.
        var payload = new
        {
            notice.AppointmentId,
            notice.ServiceName,
            notice.StartAt,
            notice.HomeVisit,
            notice.FromTeam
        };

        return _hubContext.Clients.Group(AppointmentHub.GroupOf(tenantId)).SendAsync("AppointmentRequested", payload);
    }

    public Task NotifyAppointmentCancelled(Guid tenantId, AppointmentCancelledNotice notice)
    {
        // What it was for and when; who cancelled it stays in the agenda (Fase 29O).
        var payload = new
        {
            notice.AppointmentId,
            notice.ServiceName,
            notice.StartAt,
            notice.HomeVisit,
            notice.FromTeam,
            notice.WasRequest
        };

        return _hubContext.Clients.Group(AppointmentHub.GroupOf(tenantId)).SendAsync("AppointmentCancelled", payload);
    }
}
