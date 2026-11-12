using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Orders.CancellationNotices;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.WaitingNotices;

/// <summary>
/// After an order of the online menu waited 15 minutes to be accepted (Fase 31K, ADR 0091), the email of each member who gets
/// the emails of the online orders (those of the Fase 30B): one message per member in the outbox
/// (<see cref="OnlineOrderWaitingEmailEvent"/>), saved with the record that this ran, so each is sent and retried on its own.
/// </summary>
public sealed class OnlineOrderWaitingEmailsHandler : IOutboxEventHandler<OnlineOrderWaitingEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantAccessService _access;
    private readonly IOutbox _outbox;

    public OnlineOrderWaitingEmailsHandler(IApplicationDbContext context, ITenantAccessService access, IOutbox outbox)
    {
        _context = context;
        _access = access;
        _outbox = outbox;
    }

    public async Task HandleAsync(OnlineOrderWaitingEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        foreach (var recipient in await OrderCancellationEmailRecipients.ListAsync(
                     _context, _access, domainEvent.TenantId, null, cancellationToken))
            _outbox.Add(new OnlineOrderWaitingEmailEvent(domainEvent.OrderId, recipient.UserId), domainEvent.TenantId);
    }
}

/// <summary>
/// Sends one member the reminder of an online order still waiting to be accepted (Fase 31K, ADR 0091), from the platform's
/// provider. It checks again that the order still waits and that the member still gets them and may confirm orders, as any may
/// have changed since it was queued; then nothing is sent. A failure of the provider throws, and the outbox retries this
/// member's email alone.
/// </summary>
public sealed class OnlineOrderWaitingEmailHandler : IOutboxEventHandler<OnlineOrderWaitingEmailEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantAccessService _access;
    private readonly IEmailService _email;
    private readonly IAppLinks _links;

    public OnlineOrderWaitingEmailHandler(IApplicationDbContext context, ITenantAccessService access, IEmailService email, IAppLinks links)
    {
        _context = context;
        _access = access;
        _email = email;
        _links = links;
    }

    public async Task HandleAsync(OnlineOrderWaitingEmailEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        if (context.TenantId is not { } tenantId)
            return;

        // Still waiting as the bell counts it: accepted, declined or cancelled since, there is nothing to remind.
        var order = await _context.Orders
            .AsNoTracking()
            .Where(o => o.Id == domainEvent.OrderId && o.TenantId == tenantId
                && o.Channel == OrderChannel.Online && o.Status == OrderStatus.Pending && !o.CreatedByBusiness
                && o.TableNumber == null && o.OpenTableId == null)
            .Select(o => new { o.OrderNumber, o.CustomerName, o.Total })
            .FirstOrDefaultAsync(cancellationToken);
        if (order is null)
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
        var (subject, body) = OnlineOrderWaitingEmail.Compose(business, order.CustomerName, order.OrderNumber, order.Total, _links.BaseUrl);
        await _email.SendEmailAsync(recipient.Email, subject, body);
    }
}
