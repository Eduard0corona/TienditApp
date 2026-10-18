using FoodParkDigital.Domain.Reviews.Entities;
using FoodParkDigital.Domain.Reviews.Enums;

namespace FoodParkDigital.Application.Reviews;

/// <summary>
/// How customers rate a business (Fase 24H): how many published reviews it has and their average, with one decimal, only
/// from <see cref="BusinessReview.MinForAverage"/> reviews on: one or two say little.
/// </summary>
public class RatingSummaryDto
{
    public int Count { get; set; }

    /// <summary>The average of their stars, rounded to one decimal; null with fewer than three reviews.</summary>
    public decimal? Average { get; set; }

    /// <summary>The summary of <paramref name="count"/> reviews whose stars add up to <paramref name="stars"/>.</summary>
    public static RatingSummaryDto From(int count, int stars) => new()
    {
        Count = count,
        Average = count >= BusinessReview.MinForAverage
            ? Math.Round((decimal)stars / count, 1, MidpointRounding.AwayFromZero)
            : null
    };
}

/// <summary>A review as customers see it in the store (Fase 24H): never who wrote it beyond «Mariana O.».</summary>
public class PublicReviewDto
{
    public Guid Id { get; set; }
    public string AuthorName { get; set; } = null!;
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset WrittenAt { get; set; }
    /// <summary>Its author changed it after writing it.</summary>
    public bool Edited { get; set; }
    /// <summary>The business's answer.</summary>
    public string? Reply { get; set; }
    public DateTimeOffset? RepliedAt { get; set; }
}

/// <summary>The reviews of a store, newest first, with how customers rate it.</summary>
public class PublicReviewsDto
{
    public RatingSummaryDto Summary { get; set; } = null!;
    public List<PublicReviewDto> Items { get; set; } = [];
    public int PageNumber { get; set; }
    public bool HasNextPage { get; set; }
}

/// <summary>A review the signed-in customer wrote, with the business it is about.</summary>
public class MyReviewDto
{
    /// <summary>Its store: /m/{slug}.</summary>
    public string Slug { get; set; } = null!;
    public string BusinessName { get; set; } = null!;
    /// <summary>Its logo, 256 px; none without one.</summary>
    public string? LogoUrl { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset WrittenAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public string? Reply { get; set; }
    public DateTimeOffset? RepliedAt { get; set; }
    /// <summary>The platform took it down: only its author sees it, and why (<see cref="HiddenReason"/>).</summary>
    public bool Hidden { get; set; }
    public ReviewConcern? HiddenReason { get; set; }
}

/// <summary>Whether the signed-in customer may review a business, and the review they already wrote.</summary>
public class MyReviewOfBusinessDto
{
    /// <summary>A customer account with an order received or an appointment attended at the business.</summary>
    public bool CanReview { get; set; }
    public MyReviewDto? Review { get; set; }
}

/// <summary>A review of the business as its team sees it in Reseñas: with its report and what the platform decided.</summary>
public class BusinessReviewDto
{
    public Guid Id { get; set; }
    public string AuthorName { get; set; } = null!;
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset WrittenAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public string? Reply { get; set; }
    public DateTimeOffset? RepliedAt { get; set; }
    public ReviewStatus Status { get; set; }
    /// <summary>When the business reported it; null when nothing is pending with the platform.</summary>
    public DateTimeOffset? ReportedAt { get; set; }
    public ReviewConcern? ReportReason { get; set; }
    public ReviewConcern? HiddenReason { get; set; }
    /// <summary>The platform looked at it before (it kept, hid or restored it).</summary>
    public DateTimeOffset? ModeratedAt { get; set; }
}

/// <summary>The reviews of the business in Reseñas, with how customers rate it and what is waiting.</summary>
public class BusinessReviewsDto
{
    public RatingSummaryDto Summary { get; set; } = null!;
    /// <summary>Published reviews without an answer.</summary>
    public int Unanswered { get; set; }
    /// <summary>Reviews the business reported that wait for the platform.</summary>
    public int Reported { get; set; }
    public Common.Models.PaginatedList<BusinessReviewDto> Reviews { get; set; } = null!;
}

/// <summary>A review for the platform to look at, with the business it is about.</summary>
public class PlatformReviewDto : BusinessReviewDto
{
    public Guid TenantId { get; set; }
    public string BusinessName { get; set; } = null!;
    public string BusinessSlug { get; set; } = null!;
}

/// <summary>Which reviews of the business Reseñas lists.</summary>
public enum BusinessReviewsFilter
{
    All = 0,
    /// <summary>Published, without an answer.</summary>
    Unanswered = 1,
    /// <summary>Reported, waiting for the platform.</summary>
    Reported = 2,
    /// <summary>Taken down by the platform.</summary>
    Hidden = 3
}

/// <summary>Which reviews the platform lists.</summary>
public enum PlatformReviewsFilter
{
    /// <summary>Reported by their business, the oldest report first.</summary>
    Reported = 0,
    /// <summary>Taken down, the latest first.</summary>
    Hidden = 1
}
