using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Orders.EventHandlers;

public class OrderCancelledEventHandler : INotificationHandler<OrderCancelledEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOrderNotificationService _notificationService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<OrderCancelledEventHandler> _logger;

    public OrderCancelledEventHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderNotificationService notificationService,
        IWhatsAppService whatsAppService,
        ILogger<OrderCancelledEventHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _notificationService = notificationService;
        _whatsAppService = whatsAppService;
        _logger = logger;
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

        // Send WhatsApp notification
        try
        {
            var order = await _context.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == notification.OrderId, cancellationToken);

            if (order?.CustomerPhone is not null)
            {
                await _whatsAppService.SendTemplateMessageAsync(
                    notification.TenantId,
                    order.CustomerPhone,
                    "order_cancelled",
                    "es_MX",
                    [order.OrderNumber, notification.Reason ?? "Sin razón especificada"],
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WhatsApp notification for order {OrderId}", notification.OrderId);
        }
    }
}
