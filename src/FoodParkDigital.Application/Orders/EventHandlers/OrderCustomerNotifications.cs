using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Exceptions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Orders.EventHandlers;

/// <summary>
/// WhatsApp templates that tell the customer about their order, sent by the outbox dispatcher after the
/// commit. A transient failure (timeout, throttling, provider error) is retried with backoff; a rejected
/// message or a business without WhatsApp is logged and dropped.
/// </summary>
public sealed class OrderCustomerNotifications :
    IOutboxEventHandler<OrderCreatedEvent>,
    IOutboxEventHandler<OrderStatusChangedEvent>,
    IOutboxEventHandler<OrderCancelledEvent>,
    IOutboxEventHandler<ServiceOrderPieceStatusChangedEvent>
{
    private const string Language = "es_MX";

    private readonly IApplicationDbContext _context;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<OrderCustomerNotifications> _logger;

    public OrderCustomerNotifications(
        IApplicationDbContext context,
        IWhatsAppService whatsAppService,
        ILogger<OrderCustomerNotifications> logger)
    {
        _context = context;
        _whatsAppService = whatsAppService;
        _logger = logger;
    }

    public async Task HandleAsync(OrderCreatedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        var order = await FindOrderAsync(domainEvent.OrderId, includePieces: false, cancellationToken);
        if (order?.CustomerPhone is null)
            return;

        await SendAsync(
            domainEvent.TenantId,
            order.CustomerPhone,
            "order_confirmed",
            [domainEvent.OrderNumber, OrderStatusLabels.FormatMoney(domainEvent.Total)],
            domainEvent.OrderId,
            cancellationToken);
    }

    public async Task HandleAsync(OrderStatusChangedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        // A counter sale closed when paid: the customer is at the counter and needs no message.
        if (!domainEvent.NotifyCustomer)
            return;

        var order = await FindOrderAsync(domainEvent.OrderId, includePieces: true, cancellationToken);
        if (order?.CustomerPhone is null)
            return;

        if (order.OrderType == OrderType.Service && !ShouldNotifyServiceChecklist(order, domainEvent.NewStatus))
            return;

        await SendAsync(
            domainEvent.TenantId,
            order.CustomerPhone,
            "order_status_update",
            [order.OrderNumber, OrderStatusLabels.ToSpanish(domainEvent.NewStatus)],
            domainEvent.OrderId,
            cancellationToken);
    }

    public async Task HandleAsync(OrderCancelledEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        var order = await FindOrderAsync(domainEvent.OrderId, includePieces: false, cancellationToken);
        if (order?.CustomerPhone is null)
            return;

        await SendAsync(
            domainEvent.TenantId,
            order.CustomerPhone,
            "order_cancelled",
            [order.OrderNumber, domainEvent.Reason ?? "Sin razón especificada"],
            domainEvent.OrderId,
            cancellationToken);
    }

    public async Task HandleAsync(ServiceOrderPieceStatusChangedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        if (domainEvent.NewStatus is not (ServiceOrderPieceStatus.InProgress or ServiceOrderPieceStatus.Cancelled))
            return;

        var order = await FindOrderAsync(domainEvent.OrderId, includePieces: true, cancellationToken);
        var piece = order?.ServicePieces.FirstOrDefault(x => x.Id == domainEvent.PieceId);
        if (order is null || piece is null || string.IsNullOrWhiteSpace(order.CustomerPhone) || !piece.NotifyCustomer)
            return;

        var statusLabel = domainEvent.NewStatus == ServiceOrderPieceStatus.InProgress ? "En progreso" : "Cancelado";
        await SendAsync(
            domainEvent.TenantId,
            order.CustomerPhone,
            "order_status_update",
            [order.OrderNumber, $"{piece.ItemName}: {statusLabel}"],
            domainEvent.OrderId,
            cancellationToken);
    }

    private Task<Order?> FindOrderAsync(Guid orderId, bool includePieces, CancellationToken cancellationToken)
    {
        var query = _context.Orders.AsNoTracking();
        if (includePieces)
            query = query.Include(o => o.ServicePieces);
        return query.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
    }

    private async Task SendAsync(
        Guid tenantId,
        string phone,
        string template,
        string[] parameters,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var result = await _whatsAppService.SendTemplateMessageAsync(tenantId, phone, template, Language, parameters, cancellationToken);
        if (result.IsSuccess)
            return;

        if (result.IsTransient)
            throw new TransientDeliveryException($"WhatsApp template {template} for order {orderId}: {result.Error}");

        _logger.LogWarning(
            "WhatsApp template {Template} for order {OrderId} was not sent and will not be retried: {Error}",
            template,
            orderId,
            result.Error);
    }

    private static bool ShouldNotifyServiceChecklist(Order order, OrderStatus newStatus)
    {
        var trackablePieces = order.ServicePieces
            .Where(x => x.IsTrackable)
            .ToList();

        if (trackablePieces.Count == 0)
            return true;

        if (newStatus != OrderStatus.Completed)
            return false;

        if (!trackablePieces.Any(x => x.NotifyCustomer))
            return false;

        var nonCancelled = trackablePieces
            .Where(x => x.Status != ServiceOrderPieceStatus.Cancelled)
            .ToList();

        return nonCancelled.Count > 0
            && nonCancelled.All(x => x.Status == ServiceOrderPieceStatus.Completed);
    }
}
