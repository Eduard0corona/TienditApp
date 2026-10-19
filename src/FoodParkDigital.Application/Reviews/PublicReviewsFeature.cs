using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Domain.Reviews.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Reviews;

/// <summary>
/// The published reviews of a store (Fase 24H), newest first, <see cref="ReviewRules.PublicPageSize"/> at a time, with
/// how customers rate it; null when the store does not exist or its menu is not published.
/// </summary>
[AllowAnonymousRequest("Reseñas de una tienda publicada: solo lo que sus clientes escribieron y su respuesta, sin datos de las cuentas.")]
public record GetPublicReviewsQuery(string Slug, int PageNumber = 1) : IRequest<PublicReviewsDto?>;

public class GetPublicReviewsQueryValidator : AbstractValidator<GetPublicReviewsQuery>
{
    /// <summary>A page far beyond any store's reviews: a crawler walking pages stops there.</summary>
    public const int MaxPage = 500;

    public GetPublicReviewsQueryValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(ReviewRules.MaxSlugLength);
        RuleFor(x => x.PageNumber).InclusiveBetween(1, MaxPage);
    }
}

public class GetPublicReviewsQueryHandler : IRequestHandler<GetPublicReviewsQuery, PublicReviewsDto?>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetPublicReviewsQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PublicReviewsDto?> Handle(GetPublicReviewsQuery request, CancellationToken cancellationToken)
    {
        var business = await PublicMenuTenant.FindAsync(_context, request.Slug, cancellationToken);
        if (business is null || business.Settings!.OnlineMenuMode == OnlineMenuMode.Off)
            return null;

        // A public request has no business: its reviews are read through the filter of the store's business.
        _tenantService.SetTenant(business.Id);
        var summary = await ReviewRules.SummaryAsync(_context, cancellationToken) ?? RatingSummaryDto.From(0, 0);
        var size = ReviewRules.PublicPageSize;
        var items = await _context.BusinessReviews
            .AsNoTracking()
            .Where(r => r.Status == ReviewStatus.Published)
            .OrderByDescending(r => r.WrittenAt).ThenBy(r => r.Id)
            .Skip((request.PageNumber - 1) * size)
            .Take(size)
            .Select(ReviewRules.ToPublic)
            .ToListAsync(cancellationToken);
        return new PublicReviewsDto
        {
            Summary = summary,
            Items = items,
            PageNumber = request.PageNumber,
            HasNextPage = request.PageNumber * size < summary.Count
        };
    }
}
