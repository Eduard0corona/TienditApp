using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Tenants;

/// <summary>
/// One opening of a day, in the business's own time. A closing at or before the opening ends the next day
/// (18:00 to 02:00), and the same time for both means 24 hours (00:00 to 00:00 is the whole day).
/// </summary>
public sealed record OpeningInterval(DayOfWeek Day, TimeOnly Opens, TimeOnly Closes)
{
    public bool EndsNextDay => Closes <= Opens;
}

/// <summary>Whether the business is open at a moment, and when it closes or opens next (in its own time).</summary>
public sealed record OpeningStatus(bool IsOpen, DateTime? ClosesAt, DateTime? OpensAt);

/// <summary>
/// When the business attends, by day of the week: up to two openings a day (a break at noon). Days without
/// openings are closed. Only the business's settings hold it, so it is a value: changing it replaces it.
/// </summary>
public sealed class OpeningHours : ValueObject
{
    public const int MaxIntervalsPerDay = 2;

    private OpeningHours(IReadOnlyList<OpeningInterval> intervals)
    {
        Intervals = intervals;
    }

    /// <summary>By day (Sunday first, as <see cref="DayOfWeek"/>) and opening time.</summary>
    public IReadOnlyList<OpeningInterval> Intervals { get; }

    public static OpeningHours Create(IEnumerable<OpeningInterval> intervals)
    {
        var sorted = intervals.OrderBy(i => i.Day).ThenBy(i => i.Opens).ToList();
        if (sorted.Count == 0)
            throw new DomainException("Marca al menos un día abierto, o quita el horario.");
        if (sorted.Any(i => !Enum.IsDefined(i.Day)))
            throw new DomainException("El día del horario no existe.");

        foreach (var day in sorted.GroupBy(i => i.Day))
        {
            var openings = day.ToList();
            if (openings.Count > MaxIntervalsPerDay)
                throw new DomainException($"El {NameOf(day.Key)} admite como máximo dos horarios.");
            // Only the last opening of a day may end the next day; the next one starts once the previous closes.
            for (var index = 1; index < openings.Count; index++)
            {
                var previous = openings[index - 1];
                if (previous.EndsNextDay || openings[index].Opens < previous.Closes)
                    throw new DomainException($"Los horarios del {NameOf(day.Key)} se enciman.");
            }
        }

        foreach (var late in sorted.Where(i => i.EndsNextDay))
        {
            var nextDay = (DayOfWeek)(((int)late.Day + 1) % 7);
            var nextOpening = sorted.FirstOrDefault(i => i.Day == nextDay);
            if (nextOpening is not null && nextOpening.Opens < late.Closes)
                throw new DomainException($"El horario del {NameOf(late.Day)} termina después de que abre el {NameOf(nextDay)}.");
        }

        return new OpeningHours(sorted);
    }

    /// <summary>The openings of a day of the week, by opening time.</summary>
    public IReadOnlyList<OpeningInterval> On(DayOfWeek day) => Intervals.Where(i => i.Day == day).ToList();

    /// <summary>
    /// At a moment of the business's day (its local date and time): open from an opening until its closing, with
    /// openings that follow each other joined. Never closes when it is open all the time. The same evaluation as
    /// <see cref="BusinessSchedule"/>, without special days.
    /// </summary>
    public OpeningStatus StatusAt(DateTime localNow)
        => BusinessSchedule.Create(this, [])!.StatusAtWallClock(localNow).ToOpeningStatus();

    public static string NameOf(DayOfWeek day) => SpanishCalendar.DayName(day);

    protected override IEnumerable<object?> GetEqualityComponents() => Intervals;
}
