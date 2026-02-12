using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Events;
using MediatR;

namespace FoodParkDigital.Application.Orders.EventHandlers;

public class OrderCreatedEventHandler : INotificationHandler<OrderCreatedEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOrderNotificationService _notificationService;

    public OrderCreatedEventHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderNotificationService notificationService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _notificationService = notificationService;
    }

    public async Task Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        // Create audit log
        var auditLog = new AuditLog(
            tenantId: notification.TenantId,
            entityName: nameof(Order),
            entityId: notification.OrderId.ToString(),
            action: "OrderCreated",
            performedBy: _currentUserService.UserName ?? "system",
            metadata: JsonSerializer.Serialize(new
            {
                notification.OrderNumber,
                notification.BusinessType,
                notification.Total
            }));

        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync(cancellationToken);

        // Send real-time notification
        await _notificationService.NotifyOrderCreated(
            notification.TenantId,
            notification.OrderId,
            notification.OrderNumber,
            notification.BusinessType,
            notification.Total);
    }
}
