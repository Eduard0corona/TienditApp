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
/// the end of the visit until its quote was sent. One sent before the visit ended waited nothing. Since the Fase 29W (ADR
/// 0077), a visit accepted that went <see cref="NeverQuotedAfterHours"/> past its end without its quote sent counts among
/// the slowest when nothing came of it for its customer: a quote started and never sent, or neither a quote nor a charge.
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

    /// <summary>
    /// How long after its end a visit without its quote sent counts among the slowest (Fase 29W): a week, the last limit, after
    /// which a quote sent would count among them too.
    /// </summary>
    public static int NeverQuotedAfterHours => LimitHours[^1];

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

    /// <summary>
    /// Its place in the order of Explorar (Fase 29W, ADR 0077), on the scale of <c>ResponseTimes.Order</c>: its first two
    /// tramos with the first two of answering, within a week with the last of them (within a day), and a week or more with the
    /// slowest. In the middle go those that say nothing yet.
    /// </summary>
    public static int Order(QuoteTime typical) => typical switch
    {
        QuoteTime.WithinADay => 0,
        QuoteTime.WithinThreeDays => 1,
        QuoteTime.WithinAWeek => 3,
        _ => 4
    };
}
