namespace FoodParkDigital.Domain.Appointments.OnlineBooking;

/// <summary>How soon a business answers the requests its customers make online, as Explorar says it (Fase 29J, ADR 0064).</summary>
public enum ResponseTime
{
    WithinAnHour = 0,
    WithinThreeHours = 1,
    WithinTwelveHours = 2,
    WithinADay = 3,
    /// <summary>More than a day, or not at all: Explorar says nothing and puts it after the rest of its group.</summary>
    Slower = 4
}

/// <summary>
/// How long the customers of a business waited for an answer to the requests they made online (Fase 29J, ADR 0064): an
/// appointment booked online (Fase 24D) or a visit requested (Fase 29B), from when it arrived until the business accepted
/// or declined it. One the business let pass (its time began unanswered) is the slowest; one still waiting, or that its
/// customer cancelled first, does not count yet.
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

    /// <summary>The upper limit of each <see cref="ResponseTime"/> but the last, in its order.</summary>
    public static readonly IReadOnlyList<TimeSpan> Limits =
        [TimeSpan.FromHours(1), TimeSpan.FromHours(3), TimeSpan.FromHours(12), TimeSpan.FromDays(1)];

    /// <summary>The <see cref="ResponseTime"/> of one answer, after its customer waited <paramref name="waited"/>.</summary>
    public static ResponseTime Of(TimeSpan waited)
    {
        for (var i = 0; i < Limits.Count; i++)
        {
            if (waited <= Limits[i])
                return (ResponseTime)i;
        }

        return ResponseTime.Slower;
    }

    /// <summary>
    /// Within what it answers: the soonest <see cref="ResponseTime"/> that holds at least half of its requests, those let pass
    /// as the slowest. Null with fewer than <see cref="MinRequests"/>.
    /// </summary>
    /// <param name="counts">How many requests answered within each <see cref="ResponseTime"/>, in its order; the last one
    /// also counts those let pass.</param>
    public static ResponseTime? Typical(IReadOnlyList<int> counts)
    {
        if (counts.Count != Limits.Count + 1)
            throw new ArgumentException("One count for each response time.", nameof(counts));
        if (counts.Any(count => count < 0))
            throw new ArgumentOutOfRangeException(nameof(counts), "A count is never negative.");

        var total = counts.Sum();
        if (total < MinRequests)
            return null;

        // At least half: the request in the middle, the faster one of the two in the middle when they are even.
        var half = (total + 1) / 2;
        var sofar = 0;
        for (var i = 0; i < counts.Count; i++)
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

    /// <summary>Its place in the order of Explorar within its group: the quickest first, one without enough requests in the middle.</summary>
    public static int Order(ResponseTime? typical) => (int)(typical ?? UnknownOrder);
}
