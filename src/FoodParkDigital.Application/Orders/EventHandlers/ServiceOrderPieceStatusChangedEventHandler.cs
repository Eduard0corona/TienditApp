using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Orders.EventHandlers;

public class ServiceOrderPieceStatusChangedEventHandler : INotificationHandler<ServiceOrderPieceStatusChangedEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IOrderNotificationService _orderNotificationService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<ServiceOrderPieceStatusChangedEventHandler> _logger;

    public ServiceOrderPieceStatusChangedEventHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IOrderNotificationService orderNotificationService,
        IWhatsAppService whatsAppService,
        ILogger<ServiceOrderPieceStatusChangedEventHandler> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _orderNotificationService = orderNotificationService;
        _whatsAppService = whatsAppService;
        _logger = logger;
    }

    public async Task Handle(ServiceOrderPieceStatusChangedEvent notification, CancellationToken cancellationToken)
    {
        var auditLog = new AuditLog(
            tenantId: notification.TenantId,
            entityName: "ServiceOrderPiece",
            entityId: notification.PieceId.ToString(),
            action: "ServiceOrderPieceStatusChanged",
            performedBy: _currentUserService.UserName ?? "system",
            metadata: JsonSerializer.Serialize(new
            {
                notification.OrderId,
                notification.PreviousStatus,
                notification.NewStatus
            }));
        _context.AuditLogs.Add(auditLog);

        var order = await _context.Orders
            .Include(x => x.ServicePieces)
            .FirstOrDefaultAsync(x => x.Id == notification.OrderId, cancellationToken);
        var piece = order?.ServicePieces.FirstOrDefault(x => x.Id == notification.PieceId);
        if (order is null || piece is null)
        {
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        // Inventory consumption already happened inside UpdateServiceOrderPieceStatusCommand.
        await _context.SaveChangesAsync(cancellationToken);
        await _orderNotificationService.NotifyServiceBoardPieceUpdated(
            order.TenantId,
            order.Id,
            piece.Id,
            piece.Status,
            piece.AssignedToUserId);

        if (string.IsNullOrWhiteSpace(order.CustomerPhone) || !piece.NotifyCustomer)
            return;

        if (notification.NewStatus is not (ServiceOrderPieceStatus.InProgress or ServiceOrderPieceStatus.Cancelled))
            return;

        var statusLabel = notification.NewStatus == ServiceOrderPieceStatus.InProgress
            ? "En progreso"
            : "Cancelado";

        try
        {
            await _whatsAppService.SendTemplateMessageAsync(
                notification.TenantId,
                order.CustomerPhone,
                "order_status_update",
                "es_MX",
                [order.OrderNumber, $"{piece.ItemName}: {statusLabel}"],
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send WhatsApp piece status notification for order {OrderId} piece {PieceId}",
                notification.OrderId,
                notification.PieceId);
        }
    }
}
