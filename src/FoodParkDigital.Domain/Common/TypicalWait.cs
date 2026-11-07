namespace FoodParkDigital.Domain.Common;

/// <summary>
/// The typical wait of a business, as Explorar says it: how soon it answers its requests (Fase 29J), sends its quotes
/// (Fase 29S) or confirms its orders (Fase 29T). Of its waits counted by limit, the soonest limit that holds at least half.
/// </summary>
public static class TypicalWait
{
    /// <summary>
    /// The soonest limit that holds at least half of the waits, the slowest counted last: the wait in the middle, the faster
    /// one of the two in the middle when they are even.
    /// </summary>
    /// <param name="counts">How many waits within each limit, in its order, and the slowest last: one more than the limits.</param>
    /// <param name="limits">How many limits there are.</param>
    /// <param name="minimum">Fewer waits than these say nothing.</param>
    /// <returns>The index of that limit, <paramref name="limits"/> for the slowest, or null with fewer than <paramref name="minimum"/>.</returns>
    public static int? Bucket(IReadOnlyList<int> counts, int limits, int minimum)
    {
        if (counts.Count != limits + 1)
            throw new ArgumentException("One count for each time.", nameof(counts));
        if (counts.Any(count => count < 0))
            throw new ArgumentOutOfRangeException(nameof(counts), "A count is never negative.");

        var total = counts.Sum();
        if (total < minimum)
            return null;

        var half = (total + 1) / 2;
        var sofar = 0;
        for (var i = 0; i < limits; i++)
        {
            sofar += counts[i];
            if (sofar >= half)
                return i;
        }

        return limits;
    }
}
