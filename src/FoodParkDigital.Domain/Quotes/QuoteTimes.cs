using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Quotes;

/// <summary>
/// How soon a trade sends the quote after the visit its customer requested online, as Explorar says it (Fase 29S, ADR 0073).
/// </summary>
public enum QuoteTime
{
    WithinADay = 0,
    WithinThreeDays = 1,
    WithinAWeek = 2,
    /// <summary>A week or more: Explorar says nothing; its owner reads it in Configuración.</summary>
    Slower = 3
}

/// <summary>
/// How long the customers of a trade waited for their quote after the visit they requested online (Fase 29S, ADR 0073): from
/// the end of the visit until its quote was sent. One sent before the visit ended waited nothing. Only visits whose quote was
/// sent count: one the trade never quoted says nothing, as a request not answered yet says nothing of its answer.
/// </summary>
public static class QuoteTimes
{
    /// <summary>The quotes that count were sent in these last days, like the requests of <c>ResponseTimes</c>.</summary>
    public const int WindowDays = 90;

    /// <summary>Fewer visits than these say nothing, like fewer than three requests (Fase 29J).</summary>
    public const int MinVisits = 3;

    /// <summary>
    /// The limit each <see cref="QuoteTime"/> but the last stays under, in its order: a wait of exactly a day is not within a
    /// day. Whole hours, which the database adds to when a visit ended.
    /// </summary>
    public static readonly IReadOnlyList<int> LimitHours = [24, 72, 168];

    /// <summary>Since when the quotes count at <paramref name="now"/>: those sent in the last <see cref="WindowDays"/> days.</summary>
    public static DateTimeOffset Since(DateTimeOffset now) => now.AddDays(-WindowDays);

    /// <summary>
    /// Within what it sends its quotes: the soonest <see cref="QuoteTime"/> that holds at least half of its visits, the slowest
    /// counted last. Null with fewer than <see cref="MinVisits"/>.
    /// </summary>
    /// <param name="counts">How many visits within each <see cref="QuoteTime"/>, in its order; the last one counts the slowest.</param>
    public static QuoteTime? Typical(IReadOnlyList<int> counts)
        => TypicalWait.Bucket(counts, LimitHours.Count, MinVisits) is { } bucket ? (QuoteTime)bucket : null;

    /// <summary>What Explorar says of it: its <see cref="Typical"/> within a week, never <see cref="QuoteTime.Slower"/>.</summary>
    public static QuoteTime? Shown(QuoteTime? typical)
        => typical is QuoteTime.Slower ? null : typical;
}
