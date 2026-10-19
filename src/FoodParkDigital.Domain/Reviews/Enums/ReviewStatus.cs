namespace FoodParkDigital.Domain.Reviews.Enums;

/// <summary>Whether customers see a review (Fase 24H, ADR 0030).</summary>
public enum ReviewStatus
{
    /// <summary>In the store and in its average.</summary>
    Published = 0,

    /// <summary>The platform took it down for one of the <see cref="ReviewConcern"/>s: only its author sees it, and why.</summary>
    Hidden = 1
}
