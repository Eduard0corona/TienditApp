using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Events;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Quotes.ReplyNotices;

/// <summary>
/// After a customer answers a quote from its link (Fase 29O, ADR 0069), the email of each member who gets those: one message
/// per member in the outbox (<see cref="QuoteReplyEmailEvent"/>), saved with the record that this ran, so each is sent and
/// retried on its own. None once the quote no longer has that answer, or it came over a day ago.
/// </summary>
public sealed class QuoteAnsweredEmailsHandler : IOutboxEventHandler<QuoteAnsweredFromLinkEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantAccessService _access;
    private readonly IOutbox _outbox;
    private readonly IDateTimeProvider _clock;

    public QuoteAnsweredEmailsHandler(IApplicationDbContext context, ITenantAccessService access, IOutbox outbox, IDateTimeProvider clock)
    {
        _context = context;
        _access = access;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task HandleAsync(QuoteAnsweredFromLinkEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        var paidDeposits = QuoteDeposits.PaidQuery(_context, domainEvent.TenantId);
        var quote = await _context.Quotes
            .AsNoTracking()
            .Where(q => q.Id == domainEvent.QuoteId && q.TenantId == domainEvent.TenantId)
            .Select(q => new
            {
                q.Status,
                q.ReplyChannel,
                q.ClosedAt,
                q.OrderId,
                DepositPaid = paidDeposits.Any(c => c.QuoteId == q.Id)
            })
            .FirstOrDefaultAsync(cancellationToken);
        // Paying its deposit accepted it from the link and made it its order (Fase 31N): it is still news.
        if (quote is null || !QuoteReplyEmail.Worth(quote.Status, quote.ReplyChannel, quote.ClosedAt, quote.OrderId, _clock.UtcNow,
                depositPaid: quote.Status == QuoteStatus.Accepted && quote.DepositPaid))
            return;

        foreach (var recipient in await QuoteReplyEmailRecipients.ListAsync(_context, _access, domainEvent.TenantId, null, cancellationToken))
            _outbox.Add(new QuoteReplyEmailEvent(domainEvent.QuoteId, recipient.UserId), domainEvent.TenantId);
    }
}
