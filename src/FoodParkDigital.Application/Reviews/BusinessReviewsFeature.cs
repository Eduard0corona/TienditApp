using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Reviews.Entities;
using FoodParkDigital.Domain.Reviews.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Reviews;

/// <summary>
/// Reseñas (Fase 24H): what customers say about the business, newest first, with how they rate it, the reviews without an
/// answer and the reports waiting for the platform. Hidden ones too, with why. Never who wrote them beyond «Mariana O.».
/// </summary>
[RequiresTenantManager]
public record GetBusinessReviewsQuery(BusinessReviewsFilter Filter = BusinessReviewsFilter.All, int PageNumber = 1, int PageSize = 20)
    : IRequest<BusinessReviewsDto>;

/// <summary>The business answers a review below it, or changes its answer.</summary>
[RequiresTenantManager]
public record ReplyToReviewCommand(Guid Id, string? Text) : IRequest<Result<BusinessReviewDto>>;

/// <summary>The business takes its answer down.</summary>
[RequiresTenantManager]
public record RemoveReviewReplyCommand(Guid Id) : IRequest<Result<BusinessReviewDto>>;

/// <summary>The business asks the platform to look at a review, and why. It stays published until the platform decides.</summary>
[RequiresTenantManager]
public record ReportReviewCommand(Guid Id, ReviewConcern Reason) : IRequest<Result<BusinessReviewDto>>;

public class GetBusinessReviewsQueryValidator : AbstractValidator<GetBusinessReviewsQuery>
{
    public GetBusinessReviewsQueryValidator()
    {
        RuleFor(x => x.Filter).IsInEnum();
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
    }
}

public class ReplyToReviewCommandValidator : AbstractValidator<ReplyToReviewCommand>
{
    public ReplyToReviewCommandValidator()
    {
        RuleFor(x => x.Text).NotEmpty().WithMessage(BusinessReview.ReplyRequiredMessage)
            .MaximumLength(BusinessReview.MaxReplyLength).WithMessage(BusinessReview.ReplyTooLongMessage);
    }
}

public class ReportReviewCommandValidator : AbstractValidator<ReportReviewCommand>
{
    public ReportReviewCommandValidator()
    {
        RuleFor(x => x.Reason).IsInEnum().WithMessage(BusinessReview.ConcernMessage);
    }
}

public class BusinessReviewsHandlers :
    IRequestHandler<GetBusinessReviewsQuery, BusinessReviewsDto>,
    IRequestHandler<ReplyToReviewCommand, Result<BusinessReviewDto>>,
    IRequestHandler<RemoveReviewReplyCommand, Result<BusinessReviewDto>>,
    IRequestHandler<ReportReviewCommand, Result<BusinessReviewDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public BusinessReviewsHandlers(IApplicationDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<BusinessReviewsDto> Handle(GetBusinessReviewsQuery request, CancellationToken cancellationToken)
    {
        // The business's own reviews, through its filter.
        var reviews = _context.BusinessReviews.AsNoTracking();
        var listed = request.Filter switch
        {
            BusinessReviewsFilter.Unanswered => reviews.Where(r => r.Status == ReviewStatus.Published && r.Reply == null),
            BusinessReviewsFilter.Reported => reviews.Where(r => r.ReportedAt != null),
            BusinessReviewsFilter.Hidden => reviews.Where(r => r.Status == ReviewStatus.Hidden),
            _ => reviews
        };

        return new BusinessReviewsDto
        {
            Summary = await ReviewRules.SummaryAsync(_context, cancellationToken) ?? RatingSummaryDto.From(0, 0),
            Unanswered = await reviews.CountAsync(r => r.Status == ReviewStatus.Published && r.Reply == null, cancellationToken),
            Reported = await reviews.CountAsync(r => r.ReportedAt != null, cancellationToken),
            Reviews = await PaginatedList<BusinessReviewDto>.CreateAsync(
                listed.OrderByDescending(r => r.WrittenAt).ThenBy(r => r.Id).Select(ReviewRules.ToBusiness),
                request.PageNumber, request.PageSize, cancellationToken)
        };
    }

    public Task<Result<BusinessReviewDto>> Handle(ReplyToReviewCommand request, CancellationToken cancellationToken)
        => ChangeAsync(request.Id, review => review.ReplyWith(request.Text, _clock.UtcNow), cancellationToken);

    public Task<Result<BusinessReviewDto>> Handle(RemoveReviewReplyCommand request, CancellationToken cancellationToken)
        => ChangeAsync(request.Id, review => review.RemoveReply(), cancellationToken);

    public Task<Result<BusinessReviewDto>> Handle(ReportReviewCommand request, CancellationToken cancellationToken)
        => ChangeAsync(request.Id, review => review.Report(request.Reason, _clock.UtcNow), cancellationToken);

    private async Task<Result<BusinessReviewDto>> ChangeAsync(Guid id, Action<BusinessReview> change, CancellationToken cancellationToken)
    {
        // Through the business's filter: another business's review does not exist here.
        var review = await _context.BusinessReviews.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (review is null)
            return Result.Failure<BusinessReviewDto>(ReviewRules.NotFoundMessage);
        try
        {
            change(review);
        }
        catch (DomainException ex)
        {
            return Result.Failure<BusinessReviewDto>(ex.Message);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success(ReviewRules.ForBusiness(review));
    }
}
