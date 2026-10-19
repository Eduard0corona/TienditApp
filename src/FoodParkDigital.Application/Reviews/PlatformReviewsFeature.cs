using System.Text.Json;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Audit.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Reviews.Entities;
using FoodParkDigital.Domain.Reviews.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Reviews;

/// <summary>
/// The reviews the platform looks at (Fase 24H, ADR 0030): those their business reported, the oldest report first, or those
/// it hid, the latest first; each with its business.
/// </summary>
[RequiresPlatformAdmin]
public record GetPlatformReviewsQuery(PlatformReviewsFilter Filter = PlatformReviewsFilter.Reported, int PageNumber = 1, int PageSize = 20)
    : IRequest<PaginatedList<PlatformReviewDto>>;

/// <summary>The platform takes a review down, and why: out of its store and its average. Audited in its business.</summary>
[RequiresPlatformAdmin]
public record HideReviewCommand(Guid Id, ReviewConcern Reason) : IRequest<Result>;

/// <summary>The platform looked at a report and the review stays. Audited in its business.</summary>
[RequiresPlatformAdmin]
public record KeepReviewCommand(Guid Id) : IRequest<Result>;

/// <summary>The platform puts a hidden review back. Audited in its business.</summary>
[RequiresPlatformAdmin]
public record RestoreReviewCommand(Guid Id) : IRequest<Result>;

public class GetPlatformReviewsQueryValidator : AbstractValidator<GetPlatformReviewsQuery>
{
    public GetPlatformReviewsQueryValidator()
    {
        RuleFor(x => x.Filter).IsInEnum();
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
    }
}

public class HideReviewCommandValidator : AbstractValidator<HideReviewCommand>
{
    public HideReviewCommandValidator()
    {
        RuleFor(x => x.Reason).IsInEnum().WithMessage(BusinessReview.ConcernMessage);
    }
}

public class PlatformReviewsHandlers :
    IRequestHandler<GetPlatformReviewsQuery, PaginatedList<PlatformReviewDto>>,
    IRequestHandler<HideReviewCommand, Result>,
    IRequestHandler<KeepReviewCommand, Result>,
    IRequestHandler<RestoreReviewCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public PlatformReviewsHandlers(
        IApplicationDbContext context, ICurrentTenantService tenantService, ICurrentUserService currentUser, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUser = currentUser;
        _clock = clock;
    }

    public Task<PaginatedList<PlatformReviewDto>> Handle(GetPlatformReviewsQuery request, CancellationToken cancellationToken)
    {
        // The platform reads every business's reviews: its admin is no member of them, and this list is its job.
        var reviews = _context.BusinessReviews.IgnoreQueryFilters().AsNoTracking();
        var listed = request.Filter == PlatformReviewsFilter.Hidden
            ? reviews.Where(r => r.Status == ReviewStatus.Hidden).OrderByDescending(r => r.ModeratedAt).ThenBy(r => r.Id)
            : reviews.Where(r => r.ReportedAt != null).OrderBy(r => r.ReportedAt).ThenBy(r => r.Id);
        var withBusiness = listed.Join(_context.Tenants, r => r.TenantId, t => t.Id, (r, t) => new PlatformReviewDto
        {
            Id = r.Id,
            TenantId = r.TenantId,
            BusinessName = t.Name,
            BusinessSlug = t.Slug,
            AuthorName = r.AuthorName,
            Rating = r.Rating,
            Comment = r.Comment,
            WrittenAt = r.WrittenAt,
            EditedAt = r.EditedAt,
            Reply = r.Reply,
            RepliedAt = r.RepliedAt,
            Status = r.Status,
            ReportedAt = r.ReportedAt,
            ReportReason = r.ReportReason,
            HiddenReason = r.HiddenReason,
            ModeratedAt = r.ModeratedAt
        });
        return PaginatedList<PlatformReviewDto>.CreateAsync(withBusiness, request.PageNumber, request.PageSize, cancellationToken);
    }

    public Task<Result> Handle(HideReviewCommand request, CancellationToken cancellationToken)
        => ModerateAsync(request.Id, "ReviewHidden", review => review.Hide(request.Reason, _clock.UtcNow), new { reason = request.Reason.ToString() }, cancellationToken);

    public Task<Result> Handle(KeepReviewCommand request, CancellationToken cancellationToken)
        => ModerateAsync(request.Id, "ReviewKept", review => review.Keep(_clock.UtcNow), null, cancellationToken);

    public Task<Result> Handle(RestoreReviewCommand request, CancellationToken cancellationToken)
        => ModerateAsync(request.Id, "ReviewRestored", review => review.Restore(_clock.UtcNow), null, cancellationToken);

    /// <summary>
    /// The platform's decision on one review: its business is put in scope, so the change and its audit entry are written
    /// as that business's (what the customer wrote stays out of the entry).
    /// </summary>
    private async Task<Result> ModerateAsync(Guid id, string action, Action<BusinessReview> decide, object? details, CancellationToken cancellationToken)
    {
        var tenantId = await _context.BusinessReviews
            .IgnoreQueryFilters()
            .Where(r => r.Id == id)
            .Select(r => (Guid?)r.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
        if (tenantId is null)
            return Result.Failure(ReviewRules.NotFoundMessage);

        _tenantService.SetTenant(tenantId.Value);
        // Its author may have deleted it between the two reads.
        var review = await _context.BusinessReviews.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (review is null)
            return Result.Failure(ReviewRules.NotFoundMessage);
        try
        {
            decide(review);
        }
        catch (DomainException ex)
        {
            return Result.Failure(ex.Message);
        }

        _context.AuditLogs.Add(new AuditLog(
            tenantId.Value,
            nameof(BusinessReview),
            review.Id.ToString(),
            action,
            _currentUser.UserName ?? "platform",
            details is null ? null : JsonSerializer.Serialize(details),
            _clock.UtcNow));
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
