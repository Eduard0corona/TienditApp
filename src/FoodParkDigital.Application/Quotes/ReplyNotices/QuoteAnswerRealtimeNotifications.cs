using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Quotes.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Quotes.ReplyNotices;

/// <summary>
/// Tells the live screens of whoever works the quotes that a customer accepted or rejected one from its link (Fase 29R,
/// ADR 0072). It runs in-process right after the commit and is best-effort: the email is the durable notice
/// (<see cref="QuoteAnsweredEmailsHandler"/>).
/// </summary>
public sealed class QuoteAnswerRealtimeNotifications : INotificationHandler<QuoteAnsweredFromLinkEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly IQuoteNotificationService _notifications;
    private readonly ILogger<QuoteAnswerRealtimeNotifications> _logger;

    public QuoteAnswerRealtimeNotifications(
        IApplicationDbContext context,
        IQuoteNotificationService notifications,
        ILogger<QuoteAnswerRealtimeNotifications> logger)
    {
        _context = context;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task Handle(QuoteAnsweredFromLinkEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var quote = await _context.Quotes
                .AsNoTracking()
                .Where(q => q.Id == notification.QuoteId && q.TenantId == notification.TenantId)
                .Select(q => new { q.Number, q.Revision })
                .FirstOrDefaultAsync(cancellationToken);
            if (quote is null)
                return;

            await _notifications.NotifyQuoteAnswered(
                notification.TenantId,
                new QuoteAnsweredNotice(notification.QuoteId, quote.Number, quote.Revision, notification.Accepted));
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Realtime notification failed for {EventType} {EventId}", nameof(QuoteAnsweredFromLinkEvent), notification.EventId);
        }
    }
}
