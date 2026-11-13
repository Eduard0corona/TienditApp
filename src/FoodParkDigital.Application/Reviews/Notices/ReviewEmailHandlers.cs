using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Reviews.Enums;
using FoodParkDigital.Domain.Reviews.Events;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Reviews.Notices;

/// <summary>
/// After a customer writes a new review of a business (Fase 31K, ADR 0091), the email of each member who gets them: one message
/// per member in the outbox (<see cref="BusinessReviewEmailEvent"/>), saved with the record that this ran, so each is sent and
/// retried on its own.
/// </summary>
public sealed class BusinessReviewWrittenEmailsHandler : IOutboxEventHandler<BusinessReviewWrittenEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantAccessService _access;
    private readonly IOutbox _outbox;

    public BusinessReviewWrittenEmailsHandler(IApplicationDbContext context, ITenantAccessService access, IOutbox outbox)
    {
        _context = context;
        _access = access;
        _outbox = outbox;
    }

    public async Task HandleAsync(BusinessReviewWrittenEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        foreach (var recipient in await ReviewEmailRecipients.ListAsync(_context, _access, domainEvent.TenantId, null, cancellationToken))
            _outbox.Add(new BusinessReviewEmailEvent(domainEvent.ReviewId, recipient.UserId), domainEvent.TenantId);
    }
}

/// <summary>
/// Sends one member the email of a new review (Fase 31K, ADR 0091), from the platform's provider, with what it says now. It
/// checks again that the review is still there and published, written a day ago or less, and that the member still gets them
/// and may answer the reviews, as any may have changed since it was queued; then nothing is sent. A failure of the provider
/// throws, and the outbox retries this member's email alone.
/// </summary>
public sealed class BusinessReviewEmailHandler : IOutboxEventHandler<BusinessReviewEmailEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly ITenantAccessService _access;
    private readonly IEmailService _email;
    private readonly IAppLinks _links;
    private readonly IDateTimeProvider _clock;

    public BusinessReviewEmailHandler(
        IApplicationDbContext context, ITenantAccessService access, IEmailService email, IAppLinks links, IDateTimeProvider clock)
    {
        _context = context;
        _access = access;
        _email = email;
        _links = links;
        _clock = clock;
    }

    public async Task HandleAsync(BusinessReviewEmailEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        if (context.TenantId is not { } tenantId)
            return;

        // Deleted by its author or hidden by the platform since: there is nothing to answer.
        var review = await _context.BusinessReviews
            .AsNoTracking()
            .Where(r => r.Id == domainEvent.ReviewId && r.TenantId == tenantId && r.Status == ReviewStatus.Published)
            .Select(r => new { r.AuthorName, r.Rating, r.Comment, r.WrittenAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (review is null || _clock.UtcNow - review.WrittenAt > ReviewEmail.MaxDelay)
            return;

        var recipient = (await ReviewEmailRecipients.ListAsync(_context, _access, tenantId, domainEvent.UserId, cancellationToken))
            .SingleOrDefault();
        if (recipient is null)
            return;

        var business = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstAsync(cancellationToken);
        var (subject, body) = ReviewEmail.Compose(business, review.AuthorName, review.Rating, review.Comment, _links.BaseUrl);
        await _email.SendEmailAsync(recipient.Email, subject, body);
    }
}
