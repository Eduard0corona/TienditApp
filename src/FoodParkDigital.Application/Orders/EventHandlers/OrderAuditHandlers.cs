using System.Text.Json;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Events;

namespace FoodParkDigital.Application.Orders.EventHandlers;

/// <summary>
/// Audit entries for order events, written by the outbox dispatcher. The entry is saved together with the
/// record that this handler ran, so a retry never writes it twice. The author is whoever caused the event.
/// </summary>
public sealed class OrderAuditHandlers :
    IOutboxEventHandler<OrderCreatedEvent>,
    IOutboxEventHandler<OrderStatusChangedEvent>,
    IOutboxEventHandler<OrderCancelledEvent>,
    IOutboxEventHandler<ServiceOrderPieceStatusChangedEvent>,
    IOutboxEventHandler<OrderRefundedEvent>,
    IOutboxEventHandler<TabItemCancelledEvent>
{
    private readonly IApplicationDbContext _context;

    public OrderAuditHandlers(IApplicationDbContext context)
    {
        _context = context;
    }

    public Task HandleAsync(OrderCreatedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
        => AddAsync(domainEvent.TenantId, nameof(Order), domainEvent.OrderId, "OrderCreated", context, new
        {
            domainEvent.OrderNumber,
            domainEvent.BusinessType,
            domainEvent.Total
        });

    public Task HandleAsync(OrderStatusChangedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
        => AddAsync(domainEvent.TenantId, nameof(Order), domainEvent.OrderId, "OrderStatusChanged", context, new
        {
            domainEvent.PreviousStatus,
            domainEvent.NewStatus
        });

    public Task HandleAsync(OrderCancelledEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
        => AddAsync(domainEvent.TenantId, nameof(Order), domainEvent.OrderId, "OrderCancelled", context, new
        {
            domainEvent.PreviousStatus,
            domainEvent.Reason
        });

    public Task HandleAsync(ServiceOrderPieceStatusChangedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
        => AddAsync(domainEvent.TenantId, "ServiceOrderPiece", domainEvent.PieceId, "ServiceOrderPieceStatusChanged", context, new
        {
            domainEvent.OrderId,
            domainEvent.PreviousStatus,
            domainEvent.NewStatus
        });

    public Task HandleAsync(OrderRefundedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
        => AddAsync(domainEvent.TenantId, nameof(Order), domainEvent.OrderId, "OrderRefunded", context, new
        {
            domainEvent.RefundId,
            domainEvent.Amount,
            domainEvent.Method,
            domainEvent.Reason
        });

    public Task HandleAsync(TabItemCancelledEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
        => AddAsync(domainEvent.TenantId, nameof(Order), domainEvent.OrderId, "OrderItemCancelled", context, new
        {
            domainEvent.CancellationId,
            domainEvent.OrderItemId,
            domainEvent.Round,
            domainEvent.ProductName,
            domainEvent.Quantity,
            domainEvent.Amount,
            domainEvent.Reason,
            domainEvent.ReturnedToStock
        });

    private Task AddAsync(Guid tenantId, string entityName, Guid entityId, string action, OutboxEventContext context, object metadata)
    {
        _context.AuditLogs.Add(new AuditLog(
            tenantId: tenantId,
            entityName: entityName,
            entityId: entityId.ToString(),
            action: action,
            performedBy: context.ActorName ?? "system",
            metadata: JsonSerializer.Serialize(metadata),
            timestamp: context.OccurredAt));
        return Task.CompletedTask;
    }
}
