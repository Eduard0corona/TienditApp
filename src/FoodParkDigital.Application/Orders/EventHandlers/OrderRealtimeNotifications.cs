using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Orders.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Orders.EventHandlers;

/// <summary>
/// Screen refreshes (SignalR) for order events. They run in-process right after the commit and are
/// best-effort: screens also reload when they reconnect and on their polling fallback. Durable effects
/// (audit, customer messages) are outbox handlers.
/// </summary>
public sealed class OrderRealtimeNotifications :
    INotificationHandler<OrderCreatedEvent>,
    INotificationHandler<OrderStatusChangedEvent>,
    INotificationHandler<OrderCancelledEvent>,
    INotificationHandler<ServiceOrderPieceStatusChangedEvent>,
    INotificationHandler<TabRoundSentEvent>,
    INotificationHandler<KitchenTicketChangedEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly IOrderNotificationService _notificationService;
    private readonly ILogger<OrderRealtimeNotifications> _logger;

    public OrderRealtimeNotifications(
        IApplicationDbContext context,
        IOrderNotificationService notificationService,
        ILogger<OrderRealtimeNotifications> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _logger = logger;
    }

    public Task Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
        => NotifyAsync(notification, () => _notificationService.NotifyOrderCreated(
            notification.TenantId,
            notification.OrderId,
            notification.OrderNumber,
            notification.BusinessType,
            notification.Total,
            notification.Status));

    public Task Handle(OrderStatusChangedEvent notification, CancellationToken cancellationToken)
        => NotifyAsync(notification, () => _notificationService.NotifyOrderStatusChanged(
            notification.TenantId,
            notification.OrderId,
            notification.PreviousStatus,
            notification.NewStatus));

    public Task Handle(OrderCancelledEvent notification, CancellationToken cancellationToken)
        => NotifyAsync(notification, () => _notificationService.NotifyOrderCancelled(
            notification.TenantId,
            notification.OrderId,
            notification.PreviousStatus,
            notification.Reason));

    public Task Handle(TabRoundSentEvent notification, CancellationToken cancellationToken)
        => NotifyAsync(notification, () => _notificationService.NotifyKitchenTicketsChanged(
            notification.TenantId,
            notification.OrderId,
            notification.Round));

    public Task Handle(KitchenTicketChangedEvent notification, CancellationToken cancellationToken)
        => NotifyAsync(notification, () => _notificationService.NotifyKitchenTicketsChanged(
            notification.TenantId,
            notification.OrderId,
            notification.Round));

    public Task Handle(ServiceOrderPieceStatusChangedEvent notification, CancellationToken cancellationToken)
        => NotifyAsync(notification, async () =>
        {
            var piece = await _context.ServiceOrderPieces
                .AsNoTracking()
                .Where(p => p.Id == notification.PieceId && p.OrderId == notification.OrderId)
                .Select(p => new { p.Status, p.AssignedToUserId })
                .FirstOrDefaultAsync(cancellationToken);
            if (piece is null)
                return;

            await _notificationService.NotifyServiceBoardPieceUpdated(
                notification.TenantId,
                notification.OrderId,
                notification.PieceId,
                piece.Status,
                piece.AssignedToUserId);
        });

    private async Task NotifyAsync(Domain.Common.DomainEvent domainEvent, Func<Task> notify)
    {
        try
        {
            await notify();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Realtime notification failed for {EventType} {EventId}", domainEvent.GetType().Name, domainEvent.EventId);
        }
    }
}
