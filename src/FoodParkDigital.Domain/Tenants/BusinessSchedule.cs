using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tenants;

/// <summary>
/// Whether the business is open at a moment, in its own time, and why not when a special day decides it.
/// <see cref="ClosesAt"/> and <see cref="OpensAt"/> are its wall-clock times; <see cref="ClosesAt"/> is null while it
/// stays open more than a week. Without weekly hours, a special day's hours close when they say, though the day after
/// is open by default.
/// </summary>
/// <param name="TodaySpecial">The special day of <see cref="Today"/>, if it has one.</param>
/// <param name="ClosesBy">While open: the special day whose hours set the closing (null for the weekly hours).</param>
/// <param name="ClosedFor">
/// While closed: the special day that keeps it closed now, when today is a special day or, past midnight, when
/// yesterday's took away the night that would still run.
/// </param>
/// <param name="ClosedBySpecialDay">
/// Closed by a closed special day: today is one, or yesterday was and the night it took away would still run.
/// </param>
public sealed record ScheduleStatus(
    bool IsOpen,
    DateTime? ClosesAt,
    DateTime? OpensAt,
    DateOnly Today,
    DateTime LocalNow,
    SpecialDayRule? TodaySpecial,
    SpecialDayRule? ClosesBy,
    SpecialDayRule? ClosedFor,
    bool ClosedBySpecialDay)
{
    public OpeningStatus ToOpeningStatus() => new(IsOpen, ClosesAt, OpensAt);
}

/// <summary>
/// When the business attends: its weekly hours with its special days on top. A special day replaces the openings that
/// start on its date (an opening belongs to the day it starts, so the night before still runs past midnight), and
/// without weekly hours the business is open all day except on its special days. Moments are instants, so the hour
/// repeated or skipped when the clocks change (Tijuana, Matamoros) is neither open twice nor missing.
/// </summary>
public sealed class BusinessSchedule
{
    // ClosesAt looks this far, today included: past it the business reads as open 24 hours.
    private const int NearDays = 8;

    // OpensAt looks this far: after the last special day there is a weekly opening within a week.
    private const int MaxScanDays = SpecialDay.MaxDaysAhead + 9;

    private readonly OpeningHours? _weekly;
    private readonly IReadOnlyDictionary<DateOnly, SpecialDayRule> _specials;

    private BusinessSchedule(OpeningHours? weekly, IReadOnlyDictionary<DateOnly, SpecialDayRule> specials)
    {
        _weekly = weekly;
        _specials = specials;
    }

    public bool HasWeeklyHours => _weekly is not null;

    /// <summary>Null when the business has neither weekly hours nor special days: it never closes.</summary>
    public static BusinessSchedule? Create(OpeningHours? weekly, IEnumerable<SpecialDay> specialDays)
    {
        // One rule per date (the database keeps it so); were there two, the closed one would win.
        var specials = specialDays
            .GroupBy(day => day.Date)
            .ToDictionary(
                date => date.Key,
                date => date.Select(day => day.Rule).OrderBy(rule => rule.Kind == SpecialDayKind.Closed ? 0 : 1).First());
        return weekly is null && specials.Count == 0 ? null : new BusinessSchedule(weekly, specials);
    }

    public SpecialDayRule? SpecialOn(DateOnly date) => _specials.GetValueOrDefault(date);

    /// <summary>The openings that start on a date, by opening time: its special day's, or else its weekday's.</summary>
    public IReadOnlyList<DailyOpening> OpeningsOn(DateOnly date) => SpecialOn(date) switch
    {
        null => Underlying(date),
        { Kind: SpecialDayKind.Closed } => [],
        var rule => rule.Openings
    };

    /// <summary>
    /// The openings that start on a date as instants in the business's time zone, by opening time; an opening the clocks
    /// skip whole is left out. Without weekly hours a date with no special day is open all day.
    /// </summary>
    public IReadOnlyList<(DateTimeOffset Opens, DateTimeOffset Closes)> OpeningInstantsOn(DateOnly date, TimeZoneInfo zone)
        => OpeningsOn(date)
            .OrderBy(opening => opening.Opens)
            .Select(opening => (Opens: ToInstant(date.ToDateTime(opening.Opens), zone), Closes: ToInstant(ClosingOf(date, opening), zone)))
            .Where(opening => opening.Closes > opening.Opens)
            .ToList();

    /// <summary>At an instant, in the business's time zone.</summary>
    public ScheduleStatus StatusAt(DateTimeOffset utcNow, TimeZoneInfo zone)
    {
        var localNow = DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(utcNow, zone).DateTime, DateTimeKind.Unspecified);
        var today = DateOnly.FromDateTime(localNow);
        var near = today.AddDays(NearDays).ToDateTime(TimeOnly.MinValue);
        var todaySpecial = SpecialOn(today);

        // Yesterday's last opening may still run.
        foreach (var block in Blocks(today.AddDays(-1), today.AddDays(MaxScanDays), near))
        {
            var opens = ToInstant(block.Opens, zone);
            var closes = ToInstant(block.Closes, zone);
            // Ended, or skipped whole when the clocks went forward.
            if (closes <= utcNow || closes <= opens)
                continue;
            if (opens <= utcNow)
            {
                var closesAt = block.Closes >= near ? (DateTime?)null : block.Closes;
                return new ScheduleStatus(
                    true, closesAt, null, today, localNow, todaySpecial,
                    ClosesBy: closesAt is null ? null : block.ClosesBy, ClosedFor: null, ClosedBySpecialDay: false);
            }
            return Closed(block.Opens);
        }
        return Closed(null);

        ScheduleStatus Closed(DateTime? opensAt)
        {
            var until = opensAt ?? today.AddDays(MaxScanDays).ToDateTime(TimeOnly.MinValue);
            // An opening the date would have had without its special day, between now and the next opening.
            bool TookAway(DateOnly date) => Underlying(date).Any(opening =>
                date.ToDateTime(opening.Opens) < until && ClosingOf(date, opening) > localNow);

            // Only a special day today explains the closing: a weekday the business never opens is not blamed on
            // tomorrow's holiday. Past midnight, so does yesterday's while the night it took away would still run.
            // Special hours that ended today give way to a closed day tomorrow that keeps it shut.
            var yesterday = today.AddDays(-1);
            var lastNight = SpecialOn(yesterday) is { } yesterdaySpecial && TookAway(yesterday) ? yesterdaySpecial : null;
            SpecialDayRule? closedFor = null;
            if (todaySpecial?.Kind == SpecialDayKind.Closed)
            {
                closedFor = todaySpecial;
            }
            else if (lastNight is not null)
            {
                closedFor = lastNight;
            }
            else if (todaySpecial?.Kind == SpecialDayKind.SpecialHours && TookAway(today))
            {
                var tomorrow = SpecialOn(today.AddDays(1));
                closedFor = tomorrow?.Kind == SpecialDayKind.Closed && TookAway(today.AddDays(1)) ? tomorrow : todaySpecial;
            }

            return new ScheduleStatus(
                false, null, opensAt, today, localNow, todaySpecial,
                ClosesBy: null, ClosedFor: closedFor,
                ClosedBySpecialDay: todaySpecial?.Kind == SpecialDayKind.Closed || lastNight?.Kind == SpecialDayKind.Closed);
        }
    }

    /// <summary>
    /// At a wall-clock moment, without a time zone (what <see cref="OpeningHours.StatusAt"/> receives): every time
    /// exists once.
    /// </summary>
    public ScheduleStatus StatusAtWallClock(DateTime localNow)
        => StatusAt(new DateTimeOffset(DateTime.SpecifyKind(localNow, DateTimeKind.Unspecified), TimeSpan.Zero), TimeZoneInfo.Utc);

    // The weekly openings of the date, or the whole day without weekly hours.
    private IReadOnlyList<DailyOpening> Underlying(DateOnly date) => _weekly is null
        ? [DailyOpening.AllDay]
        : _weekly.On(date.DayOfWeek).Select(interval => new DailyOpening(interval.Opens, interval.Closes)).ToList();

    private static DateTime ClosingOf(DateOnly date, DailyOpening opening)
        => (opening.EndsNextDay ? date.AddDays(1) : date).ToDateTime(opening.Closes);

    /// <summary>
    /// The openings from <paramref name="first"/> to <paramref name="last"/>, in wall-clock time, with the ones that
    /// follow each other joined, one day at a time: the scan stops at the first block the caller needs. A block that
    /// already reaches <paramref name="near"/> grows no more, and without weekly hours the whole day open by default
    /// does not stretch a special day's hours: they close when they say, and that day starts a block of its own.
    /// </summary>
    private IEnumerable<Block> Blocks(DateOnly first, DateOnly last, DateTime near)
    {
        Block? current = null;
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            var rule = SpecialOn(date);
            var openByDefault = _weekly is null && rule is null;
            foreach (var opening in OpeningsOn(date).OrderBy(opening => opening.Opens))
            {
                var opens = date.ToDateTime(opening.Opens);
                var closes = ClosingOf(date, opening);
                if (current is { } joined && opens <= joined.Closes && joined.Closes < near
                    && !(openByDefault && joined.ClosesBy is not null))
                {
                    if (closes > joined.Closes)
                        current = joined with { Closes = closes, ClosesBy = rule };
                }
                else
                {
                    if (current is { } previous)
                        yield return previous;
                    current = new Block(opens, closes, rule);
                }
            }

            // The next day's openings start at its midnight: a block that closes before can no longer grow.
            if (current is { } done && done.Closes < date.AddDays(1).ToDateTime(TimeOnly.MinValue))
            {
                yield return done;
                current = null;
            }
        }
        if (current is { } open)
            yield return open;
    }

    /// <summary>
    /// The first instant the business's clock reads <paramref name="wall"/> or later: a repeated time is its first
    /// occurrence, and a time skipped when the clocks go forward is the moment they jump.
    /// </summary>
    private static DateTimeOffset ToInstant(DateTime wall, TimeZoneInfo zone)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsAmbiguousTime(wall))
            return new DateTimeOffset(wall, zone.GetAmbiguousTimeOffsets(wall).Max());
        if (!zone.IsInvalidTime(wall))
            return new DateTimeOffset(wall, zone.GetUtcOffset(wall));

        // Inside the jump: before it the clock read less, after it more. Narrow it down to the minute.
        var after = zone.GetUtcOffset(wall.AddHours(3));
        var low = new DateTimeOffset(wall, after);
        var high = new DateTimeOffset(wall, zone.GetUtcOffset(wall.AddHours(-3)));
        while (high - low > TimeSpan.FromMinutes(1))
        {
            var middle = low + (high - low) / 2;
            if (zone.GetUtcOffset(middle) == after)
                high = middle;
            else
                low = middle;
        }
        return new DateTimeOffset(high.UtcTicks - high.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
    }

    /// <summary>A stretch the business is open, and the special day whose hours set its closing (null for weekly hours).</summary>
    private readonly record struct Block(DateTime Opens, DateTime Closes, SpecialDayRule? ClosesBy);
}
