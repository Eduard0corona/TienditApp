using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.CancellationNotices;

/// <summary>
/// After its customer cancels an online order (Fase 30B, ADR 0082), the email of each member who gets them: one message per
/// member in the outbox (<see cref="OrderCancellationEmailEvent"/>), saved with the record that this ran, so each is sent and
/// retried on its own. None for one the business cancelled.
/// </summary>
public sealed class OrderCancelledEmailsHandler : IOutboxEventHandler<OrderCancelledEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantAccessService _access;
    private readonly IOutbox _outbox;

    public OrderCancelledEmailsHandler(IApplicationDbContext context, ITenantAccessService access, IOutbox outbox)
    {
        _context = context;
        _access = access;
        _outbox = outbox;
    }

    public async Task HandleAsync(OrderCancelledEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        if (!domainEvent.ByCustomer)
            return;

        foreach (var recipient in await OrderCancellationEmailRecipients.ListAsync(
                     _context, _access, domainEvent.TenantId, null, cancellationToken))
            _outbox.Add(new OrderCancellationEmailEvent(domainEvent.OrderId, recipient.UserId), domainEvent.TenantId);
    }
}

/// <summary>
/// Sends one member the email of an online order its customer cancelled (Fase 30B, ADR 0082), from the platform's provider. It
/// checks again that the order stays cancelled by its customer, less than a day ago, and that the member still gets them and
/// may confirm orders, as either may have changed since it was queued; then nothing is sent. A failure of the provider throws,
/// and the outbox retries this member's email alone.
/// </summary>
public sealed class OrderCancellationEmailHandler : IOutboxEventHandler<OrderCancellationEmailEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantAccessService _access;
    private readonly IEmailService _email;
    private readonly IAppLinks _links;
    private readonly IDateTimeProvider _clock;

    public OrderCancellationEmailHandler(
        IApplicationDbContext context, ITenantAccessService access, IEmailService email, IAppLinks links, IDateTimeProvider clock)
    {
        _context = context;
        _access = access;
        _email = email;
        _links = links;
        _clock = clock;
    }

    public async Task HandleAsync(OrderCancellationEmailEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        if (context.TenantId is not { } tenantId)
            return;

        var order = await _context.Orders
            .AsNoTracking()
            .Where(o => o.Id == domainEvent.OrderId && o.TenantId == tenantId && o.Channel == OrderChannel.Online)
            .Select(o => new
            {
                o.Status,
                o.OrderNumber,
                o.CustomerName,
                o.Total,
                // Its history says the customer cancelled it, and when (Fase 29R).
                CancelledByCustomerAt = _context.OrderStatusHistories
                    .Where(h => h.TenantId == tenantId && h.OrderId == o.Id
                        && h.NewStatus == OrderStatus.Cancelled && h.ChangedBy == Order.CustomerPerformedBy)
                    .Max(h => (DateTimeOffset?)h.ChangedAt)
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (order is null || !OrderCancellationEmail.Worth(order.Status, order.CancelledByCustomerAt, _clock.UtcNow))
            return;

        var recipient = (await OrderCancellationEmailRecipients.ListAsync(_context, _access, tenantId, domainEvent.UserId, cancellationToken))
            .SingleOrDefault();
        if (recipient is null)
            return;

        var business = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstAsync(cancellationToken);
        var (subject, body) = OrderCancellationEmail.Compose(
            business, order.CustomerName, order.OrderNumber, order.Total, _links.BaseUrl);
        await _email.SendEmailAsync(recipient.Email, subject, body);
    }
}
