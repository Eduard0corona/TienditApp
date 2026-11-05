namespace FoodParkDigital.Domain.Appointments.OnlineBooking;

/// <summary>How soon a business answers the requests its customers make online, as Explorar says it (Fase 29J, ADR 0064).</summary>
public enum ResponseTime
{
    WithinAnHour = 0,
    WithinThreeHours = 1,
    WithinTwelveHours = 2,
    WithinADay = 3,
    /// <summary>A day or more, or not before its time: Explorar says nothing and puts it after the rest of its group.</summary>
    Slower = 4
}

/// <summary>
/// How long the customers of a business waited for an answer to the requests they made online (Fase 29J, ADR 0064): an
/// appointment booked online (Fase 24D) or a visit requested (Fase 29B), from when it arrived until the business accepted or
/// declined it, before its time began. The slowest are those answered once their time began or let pass (its time began
/// unanswered), and those that waited a day without an answer, still waiting or cancelled by their customer: their wait is
/// already a day or more. One that has waited less than a day, or that its customer cancelled sooner, does not count yet.
/// Before the first answer TienditApp kept (Fase 29J deployed), answers were not kept: what became slow before it does not
/// count, as those answered then do not.
/// </summary>
public static class ResponseTimes
{
    /// <summary>The requests that count arrived in these last days.</summary>
    public const int WindowDays = 90;

    /// <summary>Fewer requests than these say nothing, like fewer than three reviews (Fase 24H).</summary>
    public const int MinRequests = 3;

    /// <summary>
    /// Where a business without enough requests goes in the order of Explorar: with those that answer within 12 hours, so a
    /// new one, or one that takes no requests, is neither ahead of the quick ones nor behind the slow ones.
    /// </summary>
    public const ResponseTime UnknownOrder = ResponseTime.WithinTwelveHours;

    /// <summary>
    /// The limit each <see cref="ResponseTime"/> but the last stays under, in its order: a wait of exactly an hour is not
    /// within an hour. Whole hours, which the database adds to when a request arrived.
    /// </summary>
    public static readonly IReadOnlyList<int> LimitHours = [1, 3, 12, 24];

    /// <summary>Since when the requests count at <paramref name="now"/>: those that arrived in the last <see cref="WindowDays"/> days.</summary>
    public static DateTimeOffset Since(DateTimeOffset now) => now.AddDays(-WindowDays);

    /// <summary>
    /// By when a request is answered in time (Fase 29L, ADR 0066): before the time its customer asked for, and before it has
    /// waited a day, whichever comes first. Answered later, or still waiting then, it counts among the slowest (ADR 0064).
    /// The query of Explorar writes the same limit for SQL.
    /// </summary>
    /// <param name="arrivedAt">When the request arrived.</param>
    /// <param name="askedFor">The time its customer asked for (<c>RequestedStartAt</c>, or its time when it has none).</param>
    public static DateTimeOffset AnswerBy(DateTimeOffset arrivedAt, DateTimeOffset askedFor)
    {
        var aDayLater = arrivedAt.AddHours(LimitHours[^1]);
        return askedFor < aDayLater ? askedFor : aDayLater;
    }

    /// <summary>
    /// Within what it answers: the soonest <see cref="ResponseTime"/> that holds at least half of its requests, the slowest
    /// counted last. Null with fewer than <see cref="MinRequests"/>.
    /// </summary>
    /// <param name="counts">How many requests within each <see cref="ResponseTime"/>, in its order; the last one counts the
    /// slowest.</param>
    public static ResponseTime? Typical(IReadOnlyList<int> counts)
    {
        if (counts.Count != LimitHours.Count + 1)
            throw new ArgumentException("One count for each response time.", nameof(counts));
        if (counts.Any(count => count < 0))
            throw new ArgumentOutOfRangeException(nameof(counts), "A count is never negative.");

        var total = counts.Sum();
        if (total < MinRequests)
            return null;

        // At least half: the request in the middle, the faster one of the two in the middle when they are even.
        var half = (total + 1) / 2;
        var sofar = 0;
        for (var i = 0; i < LimitHours.Count; i++)
        {
            sofar += counts[i];
            if (sofar >= half)
                return (ResponseTime)i;
        }

        return ResponseTime.Slower;
    }

    /// <summary>What Explorar says of it: its <see cref="Typical"/> within a day, never <see cref="ResponseTime.Slower"/>.</summary>
    public static ResponseTime? Shown(ResponseTime? typical)
        => typical is ResponseTime.Slower ? null : typical;

    /// <summary>Its place in the order of a category of Explorar within its group: the quickest first, one without enough requests in the middle.</summary>
    public static int Order(ResponseTime? typical) => (int)(typical ?? UnknownOrder);
}
