using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Orders.EventHandlers;

public class OrderCreatedEventHandler : INotificationHandler<OrderCreatedEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOrderNotificationService _notificationService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<OrderCreatedEventHandler> _logger;

    public OrderCreatedEventHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderNotificationService notificationService,
        IWhatsAppService whatsAppService,
        ILogger<OrderCreatedEventHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _notificationService = notificationService;
        _whatsAppService = whatsAppService;
        _logger = logger;
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
                    "order_confirmed",
                    "es_MX",
                    [notification.OrderNumber, OrderStatusLabels.FormatMoney(notification.Total)],
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WhatsApp notification for order {OrderId}", notification.OrderId);
        }
    }
}
