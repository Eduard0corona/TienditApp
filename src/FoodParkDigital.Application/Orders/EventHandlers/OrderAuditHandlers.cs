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
    IOutboxEventHandler<ServiceOrderPhotoAddedEvent>,
    IOutboxEventHandler<ServiceOrderPhotoRemovedEvent>,
    IOutboxEventHandler<OrderRefundedEvent>,
    IOutboxEventHandler<TabItemCancelledEvent>
{
    private readonly IApplicationDbContext _context;

    public OrderAuditHandlers(IApplicationDbContext context)
    {
        _context = context;
    }

    // Where the order came from and, for an order of a delivery platform, the folio of its app and who charged it; for
    // an order sent to the customer's address, that it is and what it charged for it, never the address; for the sale of
    // a visit at the customer's address, what it charged for the visit.
    public Task HandleAsync(OrderCreatedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
        => AddAsync(domainEvent.TenantId, nameof(Order), domainEvent.OrderId, "OrderCreated", context, new
        {
            domainEvent.OrderNumber,
            domainEvent.BusinessType,
            domainEvent.Total,
            domainEvent.Channel,
            domainEvent.ExternalOrderNumber,
            domainEvent.CollectedBy,
            domainEvent.IsHomeDelivery,
            domainEvent.DeliveryFee,
            domainEvent.VisitFee
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

    // The photos of a service (Fase 25): which one, of what stage and its file as the server checked it; never the note
    // nor the name the device gave the file, which anyone can write.
    public Task HandleAsync(ServiceOrderPhotoAddedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
        => AddAsync(domainEvent.TenantId, nameof(Order), domainEvent.OrderId, "ServiceOrderPhotoAdded", context, new
        {
            domainEvent.PhotoId,
            domainEvent.Stage,
            domainEvent.FileUrl
        });

    // The removed photo's row is gone: the activity of the order keeps its file, who took it and when.
    public Task HandleAsync(ServiceOrderPhotoRemovedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
        => AddAsync(domainEvent.TenantId, nameof(Order), domainEvent.OrderId, "ServiceOrderPhotoRemoved", context, new
        {
            domainEvent.PhotoId,
            domainEvent.Stage,
            domainEvent.FileUrl,
            domainEvent.TakenAt,
            domainEvent.TakenBy
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
