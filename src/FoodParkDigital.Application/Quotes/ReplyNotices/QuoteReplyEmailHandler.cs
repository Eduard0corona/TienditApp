using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Events;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Quotes.ReplyNotices;

/// <summary>
/// Sends one member the email of a customer's answer to a quote (Fase 29O, ADR 0069), from the platform's provider. It checks
/// again that the quote keeps that answer and that the member still gets them and may work the quotes, as either may have
/// changed since it was queued; then nothing is sent. A failure of the provider throws, and the outbox retries this member's
/// email alone.
/// </summary>
public sealed class QuoteReplyEmailHandler : IOutboxEventHandler<QuoteReplyEmailEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantAccessService _access;
    private readonly IEmailService _email;
    private readonly IAppLinks _links;
    private readonly IDateTimeProvider _clock;

    public QuoteReplyEmailHandler(
        IApplicationDbContext context, ITenantAccessService access, IEmailService email, IAppLinks links, IDateTimeProvider clock)
    {
        _context = context;
        _access = access;
        _email = email;
        _links = links;
        _clock = clock;
    }

    public async Task HandleAsync(QuoteReplyEmailEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        if (context.TenantId is not { } tenantId)
            return;

        var quote = await _context.Quotes
            .AsNoTracking()
            .Where(q => q.Id == domainEvent.QuoteId && q.TenantId == tenantId)
            .Select(q => new
            {
                q.Status,
                q.ReplyChannel,
                q.ClosedAt,
                q.OrderId,
                q.Number,
                q.Revision,
                q.CustomerName,
                q.Total,
                LeftAMessage = q.CustomerResponse != null
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (quote is null || !QuoteReplyEmail.Worth(quote.Status, quote.ReplyChannel, quote.ClosedAt, quote.OrderId, _clock.UtcNow))
            return;

        var recipient = (await QuoteReplyEmailRecipients.ListAsync(_context, _access, tenantId, domainEvent.UserId, cancellationToken))
            .SingleOrDefault();
        if (recipient is null)
            return;

        var businessName = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstAsync(cancellationToken);
        var email = QuoteReplyEmail.Compose(
            businessName,
            quote.CustomerName,
            QuoteDeliveries.Folio(quote.Number, quote.Revision),
            quote.Status == QuoteStatus.Accepted,
            quote.Total,
            quote.LeftAMessage,
            // An accepted quote becomes a service order only where the business has services (Fase 29E).
            TenantCapabilityRules.CanCreateOrderType(recipient.Capabilities, OrderType.Service),
            domainEvent.QuoteId,
            _links.BaseUrl);

        await _email.SendEmailAsync(recipient.Email, email.Subject, email.HtmlBody);
    }
}
