using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Reviews.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Reviews;

/// <summary>
/// The reviews the signed-in customer account wrote (Fase 24H), the latest first, with the business each one is about;
/// none for an account of a business's team. Hidden ones too: their author sees why.
/// </summary>
[RequiresAuthentication]
public record GetMyReviewsQuery : IRequest<List<MyReviewDto>>;

/// <summary>
/// Whether the signed-in customer may review the business of a store, and the review they already wrote; null when the
/// store does not exist or its menu is not published.
/// </summary>
[RequiresAuthentication]
public record GetMyReviewOfBusinessQuery(string Slug) : IRequest<MyReviewOfBusinessDto?>;

/// <summary>
/// The signed-in customer writes their review of the business of a store, or changes it: one per account and business,
/// only after an order received or an appointment attended with the account.
/// </summary>
[RequiresAuthentication]
public record SaveMyReviewCommand(string Slug, int Rating, string? Comment) : IRequest<Result<MyReviewDto>>;

/// <summary>The signed-in customer deletes their review of a business, in any state of it; none there changes nothing.</summary>
[RequiresAuthentication]
public record DeleteMyReviewCommand(string Slug) : IRequest<Result>;

public class GetMyReviewOfBusinessQueryValidator : AbstractValidator<GetMyReviewOfBusinessQuery>
{
    public GetMyReviewOfBusinessQueryValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(ReviewRules.MaxSlugLength);
    }
}

public class SaveMyReviewCommandValidator : AbstractValidator<SaveMyReviewCommand>
{
    public SaveMyReviewCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(ReviewRules.MaxSlugLength);
        RuleFor(x => x.Rating).InclusiveBetween(BusinessReview.MinRating, BusinessReview.MaxRating).WithMessage(BusinessReview.RatingMessage);
        RuleFor(x => x.Comment).MaximumLength(BusinessReview.MaxCommentLength).WithMessage(BusinessReview.CommentTooLongMessage);
    }
}

public class DeleteMyReviewCommandValidator : AbstractValidator<DeleteMyReviewCommand>
{
    public DeleteMyReviewCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(ReviewRules.MaxSlugLength);
    }
}

public class CustomerReviewsHandlers :
    IRequestHandler<GetMyReviewsQuery, List<MyReviewDto>>,
    IRequestHandler<GetMyReviewOfBusinessQuery, MyReviewOfBusinessDto?>,
    IRequestHandler<SaveMyReviewCommand, Result<MyReviewDto>>,
    IRequestHandler<DeleteMyReviewCommand, Result>
{
    /// <summary>The reviews an account lists: far more than anyone writes.</summary>
    public const int MaxListed = 200;

    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;

    public CustomerReviewsHandlers(
        IApplicationDbContext context, ICurrentUserService currentUser, ICurrentTenantService tenantService, IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _tenantService = tenantService;
        _clock = clock;
    }

    public async Task<List<MyReviewDto>> Handle(GetMyReviewsQuery request, CancellationToken cancellationToken)
    {
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (account is null)
            return [];

        // The account's own reviews across businesses (by its id: none is another account's), each with its business.
        var reviews = await _context.BusinessReviews
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.UserId == account.Id)
            .OrderByDescending(r => r.WrittenAt).ThenBy(r => r.Id)
            .Take(MaxListed)
            .Join(_context.Tenants, r => r.TenantId, t => t.Id, (r, t) => new { Review = r, t.Slug, t.Name, t.LogoUrl })
            .ToListAsync(cancellationToken);
        return reviews.Select(x => ReviewRules.ForAuthor(x.Review, x.Slug, x.Name, x.LogoUrl)).ToList();
    }

    public async Task<MyReviewOfBusinessDto?> Handle(GetMyReviewOfBusinessQuery request, CancellationToken cancellationToken)
    {
        var business = await PublishedStoreAsync(request.Slug, cancellationToken);
        if (business is null)
            return null;
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (account is null)
            return new MyReviewOfBusinessDto();

        var review = await _context.BusinessReviews.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == account.Id, cancellationToken);
        return new MyReviewOfBusinessDto
        {
            CanReview = review is not null || await ReviewRules.BoughtFromAsync(_context, account.Id, cancellationToken),
            Review = review is null ? null : ReviewRules.ForAuthor(review, business.Slug, business.Name, business.LogoUrl)
        };
    }

    public async Task<Result<MyReviewDto>> Handle(SaveMyReviewCommand request, CancellationToken cancellationToken)
    {
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (account is null)
            return Result.Failure<MyReviewDto>(ReviewRules.CustomersOnlyMessage);
        var business = await PublishedStoreAsync(request.Slug, cancellationToken);
        if (business is null)
            return Result.Failure<MyReviewDto>(ReviewRules.StoreNotFoundMessage);

        var authorName = BusinessReview.AuthorNameFrom(account.FirstName, account.LastName);
        var now = _clock.UtcNow;
        var review = await _context.BusinessReviews.FirstOrDefaultAsync(r => r.UserId == account.Id, cancellationToken);
        var isNew = review is null;
        try
        {
            if (review is null)
            {
                if (!await ReviewRules.BoughtFromAsync(_context, account.Id, cancellationToken))
                    return Result.Failure<MyReviewDto>(ReviewRules.NotACustomerYetMessage);
                review = BusinessReview.Write(business.Id, account.Id, authorName, request.Rating, request.Comment, now);
                _context.BusinessReviews.Add(review);
            }
            else
            {
                review.Edit(authorName, request.Rating, request.Comment, now);
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DomainException ex)
        {
            return Result.Failure<MyReviewDto>(ex.Message);
        }
        catch (DbUpdateException) when (isNew)
        {
            // The first review written at the same time from another tab: the unique index kept that one.
            if (await WrittenMeanwhileAsync(account.Id, cancellationToken))
                return Result.Failure<MyReviewDto>(ReviewRules.WrittenMeanwhileMessage);
            throw;
        }

        return Result.Success(ReviewRules.ForAuthor(review, business.Slug, business.Name, business.LogoUrl));
    }

    public async Task<Result> Handle(DeleteMyReviewCommand request, CancellationToken cancellationToken)
    {
        var account = await CustomerAccounts.FindSignedInAsync(_context, _currentUser, cancellationToken);
        if (account is null)
            return Result.Failure(ReviewRules.CustomersOnlyMessage);

        // By its slug whatever its state: a business suspended since does not keep the customer's words.
        var slug = request.Slug.Trim().ToLowerInvariant();
        var businessId = await _context.Tenants.Where(t => t.Slug == slug).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(cancellationToken);
        if (businessId is null)
            return Result.Success();

        _tenantService.SetTenant(businessId.Value);
        var review = await _context.BusinessReviews.FirstOrDefaultAsync(r => r.UserId == account.Id, cancellationToken);
        if (review is null)
            return Result.Success();
        _context.BusinessReviews.Remove(review);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// The store a customer sees (an active business with its menu published), and from here on its business in scope:
    /// its reviews, orders and appointments are read through its filter, and a new review is its own.
    /// </summary>
    private async Task<Domain.Tenants.Entities.Tenant?> PublishedStoreAsync(string slug, CancellationToken cancellationToken)
    {
        var business = await PublicMenuTenant.FindAsync(_context, slug, cancellationToken);
        if (business is null || business.Settings!.OnlineMenuMode == OnlineMenuMode.Off)
            return null;
        _tenantService.SetTenant(business.Id);
        return business;
    }

    private Task<bool> WrittenMeanwhileAsync(Guid userId, CancellationToken cancellationToken)
    {
        // What failed to save stays tracked: only the review already in the database answers.
        foreach (var entry in _context.ChangeTracker.Entries<BusinessReview>().Where(e => e.State == EntityState.Added).ToList())
            entry.State = EntityState.Detached;
        return _context.BusinessReviews.AsNoTracking().AnyAsync(r => r.UserId == userId, cancellationToken);
    }
}
