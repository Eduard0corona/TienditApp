using System.Linq.Expressions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Reviews.Entities;
using FoodParkDigital.Domain.Reviews.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Reviews;

/// <summary>The rules every use case of reviews shares (Fase 24H, ADR 0030).</summary>
public static class ReviewRules
{
    /// <summary>No slug of a business is longer.</summary>
    public const int MaxSlugLength = 100;
    public const int PublicPageSize = 10;

    public const string CustomersOnlyMessage = "Solo una cuenta de cliente escribe reseñas.";
    public const string StoreNotFoundMessage = "Este negocio no tiene menú en línea.";
    public const string NotACustomerYetMessage = "Puedes reseñar un negocio después de recibir un pedido o asistir a una cita con él.";
    public const string NotFoundMessage = "La reseña no existe.";
    public const string WrittenMeanwhileMessage = "Ya escribiste una reseña de este negocio en otra pestaña. Vuelve a abrirla para cambiarla.";

    /// <summary>
    /// Whether the account bought from the business in scope: an order with its account that it received (delivered, or a
    /// service finished) or an appointment it booked with its account and attended. Read through the business's filter.
    /// </summary>
    public static async Task<bool> BoughtFromAsync(IApplicationDbContext context, Guid userId, CancellationToken cancellationToken)
        => await context.Orders.AnyAsync(
               o => o.CustomerUserId == userId && (o.Status == OrderStatus.Delivered || o.Status == OrderStatus.Completed),
               cancellationToken)
           || await context.Appointments.AnyAsync(
               a => a.CustomerUserId == userId && a.Status == AppointmentStatus.Completed,
               cancellationToken);

    /// <summary>How customers rate the business in scope; null without published reviews.</summary>
    public static async Task<RatingSummaryDto?> SummaryAsync(IApplicationDbContext context, CancellationToken cancellationToken)
    {
        var totals = await context.BusinessReviews
            .AsNoTracking()
            .Where(r => r.Status == ReviewStatus.Published)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Stars = g.Sum(r => r.Rating) })
            .FirstOrDefaultAsync(cancellationToken);
        return totals is null ? null : RatingSummaryDto.From(totals.Count, totals.Stars);
    }

    public static readonly Expression<Func<BusinessReview, PublicReviewDto>> ToPublic = r => new PublicReviewDto
    {
        Id = r.Id,
        AuthorName = r.AuthorName,
        Rating = r.Rating,
        Comment = r.Comment,
        WrittenAt = r.WrittenAt,
        Edited = r.EditedAt != null,
        Reply = r.Reply,
        RepliedAt = r.RepliedAt
    };

    public static readonly Expression<Func<BusinessReview, BusinessReviewDto>> ToBusiness = r => new BusinessReviewDto
    {
        Id = r.Id,
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
    };

    public static BusinessReviewDto ForBusiness(BusinessReview review) => ToBusinessCompiled(review);

    /// <summary>The review as its author sees it, with its business.</summary>
    public static MyReviewDto ForAuthor(BusinessReview review, string slug, string businessName, string? logoUrl) => new()
    {
        Slug = slug,
        BusinessName = businessName,
        LogoUrl = string.IsNullOrEmpty(logoUrl) ? null : $"{logoUrl}_sm.webp",
        Rating = review.Rating,
        Comment = review.Comment,
        WrittenAt = review.WrittenAt,
        EditedAt = review.EditedAt,
        Reply = review.Reply,
        RepliedAt = review.RepliedAt,
        Hidden = !review.IsPublished,
        HiddenReason = review.HiddenReason
    };

    private static readonly Func<BusinessReview, BusinessReviewDto> ToBusinessCompiled = ToBusiness.Compile();
}
