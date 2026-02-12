using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Events;
using MediatR;

namespace FoodParkDigital.Application.Orders.EventHandlers;

public class OrderStatusChangedEventHandler : INotificationHandler<OrderStatusChangedEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOrderNotificationService _notificationService;

    public OrderStatusChangedEventHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderNotificationService notificationService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _notificationService = notificationService;
    }

    public async Task Handle(OrderStatusChangedEvent notification, CancellationToken cancellationToken)
    {
        var auditLog = new AuditLog(
            tenantId: notification.TenantId,
            entityName: nameof(Order),
            entityId: notification.OrderId.ToString(),
            action: "OrderStatusChanged",
            performedBy: _currentUserService.UserName ?? "system",
            metadata: JsonSerializer.Serialize(new
            {
                notification.PreviousStatus,
                notification.NewStatus
            }));

        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync(cancellationToken);

        await _notificationService.NotifyOrderStatusChanged(
            notification.TenantId,
            notification.OrderId,
            notification.PreviousStatus,
            notification.NewStatus);
    }
}
