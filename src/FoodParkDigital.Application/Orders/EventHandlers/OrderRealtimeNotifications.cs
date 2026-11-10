using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Orders.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Orders.EventHandlers;

/// <summary>
/// Screen refreshes (SignalR) for order events. They run in-process right after the commit and are
/// best-effort: screens also reload when they reconnect and on their polling fallback. Durable effects
/// (audit, customer messages) are outbox handlers. Since Fase 31A (ADR 0083) «Mis pedidos» of the customer's account
/// also hears that their order was created, changed status, was cancelled or went out.
/// </summary>
public sealed class OrderRealtimeNotifications :
    INotificationHandler<OrderCreatedEvent>,
    INotificationHandler<OrderStatusChangedEvent>,
    INotificationHandler<OrderCancelledEvent>,
    INotificationHandler<ServiceOrderPieceStatusChangedEvent>,
    INotificationHandler<TabRoundSentEvent>,
    INotificationHandler<KitchenTicketChangedEvent>,
    INotificationHandler<TabItemCancelledEvent>,
    INotificationHandler<OrderDeliveryUpdatedEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly IOrderNotificationService _notificationService;
    private readonly ICustomerOrderNotificationService _customerNotifications;
    private readonly ILogger<OrderRealtimeNotifications> _logger;

    public OrderRealtimeNotifications(
        IApplicationDbContext context,
        IOrderNotificationService notificationService,
        ICustomerOrderNotificationService customerNotifications,
        ILogger<OrderRealtimeNotifications> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _customerNotifications = customerNotifications;
        _logger = logger;
    }

    public async Task Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        await NotifyAsync(notification, () => _notificationService.NotifyOrderCreated(
            notification.TenantId,
            notification.OrderId,
            notification.OrderNumber,
            notification.BusinessType,
            notification.Total,
            notification.Status,
            notification.Channel,
            notification.ExternalOrderNumber));
        await NotifyCustomerAsync(notification, notification.TenantId, notification.OrderId, cancellationToken);
    }

    public async Task Handle(OrderStatusChangedEvent notification, CancellationToken cancellationToken)
    {
        await NotifyAsync(notification, () => _notificationService.NotifyOrderStatusChanged(
            notification.TenantId,
            notification.OrderId,
            notification.PreviousStatus,
            notification.NewStatus));
        await NotifyCustomerAsync(notification, notification.TenantId, notification.OrderId, cancellationToken);
    }

    public async Task Handle(OrderCancelledEvent notification, CancellationToken cancellationToken)
    {
        await NotifyAsync(notification, async () =>
        {
            var orderNumber = await _context.Orders
                .AsNoTracking()
                .Where(o => o.Id == notification.OrderId && o.TenantId == notification.TenantId)
                .Select(o => o.OrderNumber)
                .FirstOrDefaultAsync(cancellationToken);
            await _notificationService.NotifyOrderCancelled(
                notification.TenantId,
                notification.OrderId,
                notification.PreviousStatus,
                notification.Reason,
                orderNumber ?? string.Empty,
                notification.ByCustomer);
        });
        await NotifyCustomerAsync(notification, notification.TenantId, notification.OrderId, cancellationToken);
    }

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

    // The kitchen stops preparing what was cancelled.
    public Task Handle(TabItemCancelledEvent notification, CancellationToken cancellationToken)
        => NotifyAsync(notification, () => _notificationService.NotifyKitchenTicketsChanged(
            notification.TenantId,
            notification.OrderId,
            notification.Round));

    // The trip written down or the order sent out with the courier: Pedidos reads the order again.
    public async Task Handle(OrderDeliveryUpdatedEvent notification, CancellationToken cancellationToken)
    {
        await NotifyAsync(notification, () => _notificationService.NotifyOrderUpdated(notification.TenantId, notification.OrderId));
        await NotifyCustomerAsync(notification, notification.TenantId, notification.OrderId, cancellationToken);
    }

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

    /// <summary>
    /// The account the order belongs to, if any, hears that it changed (ADR 0083): an order placed without signing in,
    /// or at the counter, has none. Apart from the business's screens, so one failing does not stop the other.
    /// </summary>
    private Task NotifyCustomerAsync(Domain.Common.DomainEvent domainEvent, Guid tenantId, Guid orderId, CancellationToken cancellationToken)
        => NotifyAsync(domainEvent, async () =>
        {
            var customerUserId = await _context.Orders
                .AsNoTracking()
                .Where(o => o.Id == orderId && o.TenantId == tenantId)
                .Select(o => o.CustomerUserId)
                .FirstOrDefaultAsync(cancellationToken);
            if (customerUserId is { } userId)
                await _customerNotifications.NotifyOrderChanged(userId, orderId);
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
