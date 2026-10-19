using FoodParkDigital.Domain.Tenants;

namespace FoodParkDigital.Domain.Appointments.OnlineBooking;

/// <summary>
/// One person a customer may book for a service (Fase 24J, ADR 0032): their own weekly hours (null when they attend all of
/// the business's) and what their open appointments hold of their time.
/// </summary>
public sealed record StaffCalendar(Guid StaffId, OpeningHours? WeeklyHours, IReadOnlyCollection<BookedInterval> Booked);

/// <summary>A time a customer may book, with the people free for it.</summary>
public sealed record StaffSlot(DateTimeOffset StartAt, IReadOnlyList<Guid> FreeStaff);

/// <summary>
/// The times a customer may book when the business says who attends online (Fase 24J, ADR 0032). The same times as
/// <see cref="BookingSlots"/> (inside the business's openings of a date, every half hour from each one, from the earliest
/// allowed on), each with the people free for all of it: they attend then (their own hours, when they have them) and none
/// of their open appointments overlaps it. An open appointment without anyone assigned takes someone too: a time is
/// offered only while more people are free than such appointments run at once.
/// </summary>
public static class StaffSlots
{
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(OnlineBookingRules.SlotStepMinutes);

    /// <summary>The free starts of the openings that start on <paramref name="date"/>, in order.</summary>
    /// <param name="staff">The people who do the service.</param>
    /// <param name="unassigned">The open appointments around the date with nobody assigned.</param>
    /// <param name="earliest">Nothing starts before it (now plus the notice).</param>
    public static IReadOnlyList<StaffSlot> FreeStarts(
        BusinessSchedule? schedule,
        TimeZoneInfo zone,
        DateOnly date,
        TimeSpan duration,
        IReadOnlyCollection<StaffCalendar> staff,
        IReadOnlyCollection<BookedInterval> unassigned,
        DateTimeOffset earliest)
    {
        if (schedule is not { HasWeeklyHours: true } || duration <= TimeSpan.Zero || staff.Count == 0)
            return [];

        var hours = staff.ToDictionary(person => person.StaffId, person => Attends(person.WeeklyHours, zone, date));
        var slots = new List<StaffSlot>();
        foreach (var (opens, closes) in schedule.OpeningInstantsOn(date, zone))
        {
            for (var start = opens; start + duration <= closes; start += Step)
            {
                if (start < earliest)
                    continue;
                var end = start + duration;
                var free = staff
                    .Where(person => Covers(hours[person.StaffId], start, end)
                        && !person.Booked.Any(interval => interval.StartAt < end && interval.EndAt > start))
                    .Select(person => person.StaffId)
                    .ToList();
                if (free.Count > BookingSlots.MaxOverlapping(unassigned, start, end))
                    slots.Add(new StaffSlot(start, free));
            }
        }
        return slots;
    }

    /// <summary>
    /// The people free for <paramref name="start"/> when it is one of the free starts (of the openings of its date or of
    /// the day before, up to <paramref name="lastDate"/> when given); none when it is not.
    /// </summary>
    public static IReadOnlyList<Guid> FreeAt(
        BusinessSchedule? schedule,
        TimeZoneInfo zone,
        DateTimeOffset start,
        TimeSpan duration,
        IReadOnlyCollection<StaffCalendar> staff,
        IReadOnlyCollection<BookedInterval> unassigned,
        DateTimeOffset earliest,
        DateOnly? lastDate = null)
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, zone).DateTime);
        foreach (var day in new[] { date, date.AddDays(-1) })
        {
            if (lastDate is not null && day > lastDate)
                continue;
            var slot = FreeStarts(schedule, zone, day, duration, staff, unassigned, earliest)
                .FirstOrDefault(candidate => candidate.StartAt == start);
            if (slot is not null)
                return slot.FreeStaff;
        }
        return [];
    }

    /// <summary>
    /// Who takes an appointment booked with whoever is free: the one with the fewest appointments that day, then by name
    /// and id, so the same state always picks the same person.
    /// </summary>
    /// <param name="appointmentsThatDay">Open appointments of each person on the day; missing means none.</param>
    public static Guid Choose(
        IReadOnlyCollection<Guid> free,
        IReadOnlyDictionary<Guid, int> appointmentsThatDay,
        IReadOnlyDictionary<Guid, string> names)
    {
        if (free.Count == 0)
            throw new ArgumentException("Someone must be free.", nameof(free));
        return free
            .OrderBy(id => appointmentsThatDay.GetValueOrDefault(id))
            .ThenBy(id => names.GetValueOrDefault(id) ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(id => id)
            .First();
    }

    /// <summary>
    /// When someone attends around a date (their openings of the day before, of the date and of the next, as instants,
    /// with the ones that follow each other joined); null when they attend whenever the business is open.
    /// </summary>
    private static IReadOnlyList<(DateTimeOffset Opens, DateTimeOffset Closes)>? Attends(OpeningHours? weekly, TimeZoneInfo zone, DateOnly date)
    {
        if (weekly is null)
            return null;
        var schedule = BusinessSchedule.Create(weekly, [])!;
        var joined = new List<(DateTimeOffset Opens, DateTimeOffset Closes)>();
        foreach (var opening in new[] { date.AddDays(-1), date, date.AddDays(1) }
            .SelectMany(day => schedule.OpeningInstantsOn(day, zone))
            .OrderBy(opening => opening.Opens))
        {
            if (joined.Count > 0 && opening.Opens <= joined[^1].Closes)
                joined[^1] = (joined[^1].Opens, opening.Closes > joined[^1].Closes ? opening.Closes : joined[^1].Closes);
            else
                joined.Add(opening);
        }
        return joined;
    }

    private static bool Covers(IReadOnlyList<(DateTimeOffset Opens, DateTimeOffset Closes)>? attends, DateTimeOffset start, DateTimeOffset end)
        => attends is null || attends.Any(opening => opening.Opens <= start && end <= opening.Closes);
}
