using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Reviews.Enums;
using FoodParkDigital.Domain.Reviews.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Reviews.Notices;

/// <summary>
/// After a business reports one of its reviews (Fase 32F, ADR 0098), the email of each admin of the platform: one message per
/// admin in the outbox (<see cref="BusinessReviewReportEmailEvent"/>), saved with the record that this ran, so each is sent and
/// retried on its own. Past <see cref="ReviewReportEmail.MaxPerBusinessPerHour"/> reports of the same business within the hour
/// before this one, none is queued: those wait in the console.
/// </summary>
public sealed class BusinessReviewReportedEmailsHandler : IOutboxEventHandler<BusinessReviewReportedEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly IOutbox _outbox;
    private readonly ILogger<BusinessReviewReportedEmailsHandler> _logger;

    public BusinessReviewReportedEmailsHandler(
        IApplicationDbContext context, IOutbox outbox, ILogger<BusinessReviewReportedEmailsHandler> logger)
    {
        _context = context;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task HandleAsync(BusinessReviewReportedEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        // Kept, hidden or deleted since: there is nothing to look at. The scope runs with the business of the review.
        var reportedAt = await _context.BusinessReviews
            .AsNoTracking()
            .Where(r => r.Id == domainEvent.ReviewId && r.TenantId == domainEvent.TenantId && r.Status == ReviewStatus.Published)
            .Select(r => r.ReportedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (reportedAt is not { } at)
            return;

        var since = at - ReviewReportEmail.BusinessWindow;
        var before = await _context.BusinessReviews
            .AsNoTracking()
            .CountAsync(
                r => r.TenantId == domainEvent.TenantId && r.ReportedAt > since && r.ReportedAt < at,
                cancellationToken);
        if (before >= ReviewReportEmail.MaxPerBusinessPerHour)
        {
            _logger.LogInformation(
                "Review {ReviewId} reported by business {TenantId}: {Count} reports of it in the hour before, no email.",
                domainEvent.ReviewId, domainEvent.TenantId, before);
            return;
        }

        foreach (var admin in await PlatformAdmins(_context, null).Select(u => u.Id).ToListAsync(cancellationToken))
            _outbox.Add(new BusinessReviewReportEmailEvent(domainEvent.ReviewId, admin), domainEvent.TenantId);
    }

    /// <summary>
    /// Who reads the reports: the platform's admins (<see cref="UserRole.SuperAdmin"/>, whom the API lets into Plataforma ›
    /// Reseñas), with their account active and their email confirmed.
    /// </summary>
    internal static IQueryable<ApplicationUser> PlatformAdmins(IApplicationDbContext context, Guid? onlyUserId)
        => context.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRole.SuperAdmin && u.IsActive && u.EmailConfirmed && u.Email != null
                && (onlyUserId == null || u.Id == onlyUserId))
            .OrderBy(u => u.Id);
}

/// <summary>
/// Sends one admin of the platform the email of a reported review (Fase 32F, ADR 0098), from the platform's provider, with what
/// it says and why it was reported now. It checks again that the review is still there, published and reported, a day ago or
/// less, and that the admin still is one, as any may have changed since it was queued; then nothing is sent. A failure of the
/// provider throws, and the outbox retries this admin's email alone.
/// </summary>
public sealed class BusinessReviewReportEmailHandler : IOutboxEventHandler<BusinessReviewReportEmailEvent>
{
    private readonly IApplicationDbContext _context;
    private readonly IEmailService _email;
    private readonly IAppLinks _links;
    private readonly IDateTimeProvider _clock;

    public BusinessReviewReportEmailHandler(IApplicationDbContext context, IEmailService email, IAppLinks links, IDateTimeProvider clock)
    {
        _context = context;
        _email = email;
        _links = links;
        _clock = clock;
    }

    public async Task HandleAsync(BusinessReviewReportEmailEvent domainEvent, OutboxEventContext context, CancellationToken cancellationToken)
    {
        if (context.TenantId is not { } tenantId)
            return;

        // Kept or hidden by the platform, or deleted by its author since: there is nothing left to decide.
        var review = await _context.BusinessReviews
            .AsNoTracking()
            .Where(r => r.Id == domainEvent.ReviewId && r.TenantId == tenantId
                && r.Status == ReviewStatus.Published && r.ReportedAt != null && r.ReportReason != null)
            .Select(r => new { r.AuthorName, r.Rating, r.Comment, ReportedAt = r.ReportedAt!.Value, Reason = r.ReportReason!.Value })
            .FirstOrDefaultAsync(cancellationToken);
        if (review is null || _clock.UtcNow - review.ReportedAt > ReviewReportEmail.MaxDelay)
            return;

        var email = await BusinessReviewReportedEmailsHandler.PlatformAdmins(_context, domainEvent.UserId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);
        if (email is null)
            return;

        var business = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstAsync(cancellationToken);
        var (subject, body) = ReviewReportEmail.Compose(
            business, review.AuthorName, review.Rating, review.Comment, review.Reason, _links.BaseUrl);
        await _email.SendEmailAsync(email, subject, body);
    }
}
