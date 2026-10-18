using FoodParkDigital.Domain.Tenants;

namespace FoodParkDigital.Domain.Appointments.OnlineBooking;

/// <summary>The time an open appointment holds in the business's agenda.</summary>
public readonly record struct BookedInterval(DateTimeOffset StartAt, DateTimeOffset EndAt);

/// <summary>
/// The times a customer may book online (Fase 24D, ADR 0025): inside the business's openings of a date (its weekly hours
/// with its special days on top), every <see cref="OnlineBookingRules.SlotStepMinutes"/> minutes from each opening, ending by
/// the time it closes, from the earliest time allowed on, and while fewer appointments than the business attends at once
/// overlap it at any moment. Without weekly hours there is nothing to offer: the business never said when it attends.
/// </summary>
public static class BookingSlots
{
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(OnlineBookingRules.SlotStepMinutes);

    /// <summary>The free starts of the openings that start on <paramref name="date"/>, in order.</summary>
    /// <param name="booked">The open appointments around the date, of the whole business.</param>
    /// <param name="capacity">Appointments the business attends at once.</param>
    /// <param name="earliest">Nothing starts before it (now plus the notice).</param>
    public static IReadOnlyList<DateTimeOffset> FreeStarts(
        BusinessSchedule? schedule,
        TimeZoneInfo zone,
        DateOnly date,
        TimeSpan duration,
        IReadOnlyCollection<BookedInterval> booked,
        int capacity,
        DateTimeOffset earliest)
    {
        if (schedule is not { HasWeeklyHours: true } || duration <= TimeSpan.Zero || capacity < 1)
            return [];

        var starts = new List<DateTimeOffset>();
        foreach (var (opens, closes) in schedule.OpeningInstantsOn(date, zone))
        {
            for (var start = opens; start + duration <= closes; start += Step)
            {
                if (start >= earliest && MaxOverlapping(booked, start, start + duration) < capacity)
                    starts.Add(start);
            }
        }
        return starts;
    }

    /// <summary>
    /// Whether <paramref name="start"/> is one of the free starts: of the openings of its date in the business, or of the
    /// day before (an opening that runs past midnight belongs to the day it starts), of a date up to
    /// <paramref name="lastDate"/> when given (the last day offered).
    /// </summary>
    public static bool IsFree(
        BusinessSchedule? schedule,
        TimeZoneInfo zone,
        DateTimeOffset start,
        TimeSpan duration,
        IReadOnlyCollection<BookedInterval> booked,
        int capacity,
        DateTimeOffset earliest,
        DateOnly? lastDate = null)
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, zone).DateTime);
        bool OffersOn(DateOnly day) => (lastDate is null || day <= lastDate)
            && FreeStarts(schedule, zone, day, duration, booked, capacity, earliest).Contains(start);
        return OffersOn(date) || OffersOn(date.AddDays(-1));
    }

    /// <summary>The most appointments running at once between <paramref name="from"/> and <paramref name="to"/>.</summary>
    public static int MaxOverlapping(IReadOnlyCollection<BookedInterval> booked, DateTimeOffset from, DateTimeOffset to)
    {
        var overlapping = booked.Where(interval => interval.StartAt < to && interval.EndAt > from).ToList();
        // How many run at once changes only where one starts: count at each of those moments (or the beginning).
        return overlapping.Count == 0
            ? 0
            : overlapping
                .Select(interval => interval.StartAt < from ? from : interval.StartAt)
                .Max(moment => overlapping.Count(interval => interval.StartAt <= moment && interval.EndAt > moment));
    }
}
