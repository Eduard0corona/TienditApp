using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Events;
using MediatR;

namespace FoodParkDigital.Application.Orders.EventHandlers;

public class OrderCancelledEventHandler : INotificationHandler<OrderCancelledEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOrderNotificationService _notificationService;

    public OrderCancelledEventHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderNotificationService notificationService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _notificationService = notificationService;
    }

    public async Task Handle(OrderCancelledEvent notification, CancellationToken cancellationToken)
    {
        var auditLog = new AuditLog(
            tenantId: notification.TenantId,
            entityName: nameof(Order),
            entityId: notification.OrderId.ToString(),
            action: "OrderCancelled",
            performedBy: _currentUserService.UserName ?? "system",
            metadata: JsonSerializer.Serialize(new
            {
                notification.PreviousStatus,
                notification.Reason
            }));

        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync(cancellationToken);

        await _notificationService.NotifyOrderCancelled(
            notification.TenantId,
            notification.OrderId,
            notification.PreviousStatus,
            notification.Reason);
    }
}
