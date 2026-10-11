using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Entities;

namespace FoodParkDigital.Domain.Tenants;

/// <summary>A period of special days as the owner reads it: from, to (one day when equal) and its rule.</summary>
public sealed record SpecialDayPeriodSnapshot(DateOnly From, DateOnly To, SpecialDayRule Rule);

/// <summary>
/// The rows a change adds, modifies and removes. <see cref="PeriodId"/> is the period the owner changed; a new rule for
/// a period already under way leaves its past dates there and moves the rest to <see cref="NewPeriodId"/>.
/// </summary>
public sealed record SpecialDayChange(
    IReadOnlyList<SpecialDay> Added,
    IReadOnlyList<SpecialDay> Modified,
    IReadOnlyList<SpecialDay> Removed,
    Guid PeriodId,
    Guid? NewPeriodId,
    SpecialDayPeriodSnapshot? Before,
    SpecialDayPeriodSnapshot? After)
{
    /// <summary>The request asked for what is already there: nothing to save or record.</summary>
    public bool IsEmpty => Added.Count == 0 && Modified.Count == 0 && Removed.Count == 0;

    internal static SpecialDayChange None(Guid periodId) => new([], [], [], periodId, null, null, null);
}

/// <summary>
/// The special days of one business, to add, change or remove a period. It works on the business's dates from today on
/// (plus every date of the period being changed): a date that already passed is history and never changes, so a period
/// under way can only end earlier, and a new rule for it starts today.
/// </summary>
public sealed class SpecialDayCalendar
{
    private readonly Guid _tenantId;
    private readonly DateOnly _today;
    private readonly List<SpecialDay> _rows;

    /// <param name="today">The business's date, in its time zone.</param>
    /// <param name="rows">Its rows dated today or later, plus every row of the period being changed.</param>
    public SpecialDayCalendar(Guid tenantId, DateOnly today, IEnumerable<SpecialDay> rows)
    {
        _rows = rows.ToList();
        if (_rows.Any(row => row.TenantId != tenantId))
            throw new InvalidOperationException("The special days of a business are changed only in its own calendar.");

        _tenantId = tenantId;
        _today = today;
    }

    private IEnumerable<SpecialDay> Upcoming => _rows.Where(row => row.Date >= _today);

    public SpecialDayChange Add(DateOnly from, DateOnly to, SpecialDayRule rule)
    {
        RequireOrder(from, to);
        RequireFromToday(from);
        RequireWithinAYear(to);
        RequireLength(from, to);

        // A second tap, or a retry, of a period already saved.
        var same = Upcoming.GroupBy(row => row.PeriodId).FirstOrDefault(period =>
            period.Min(row => row.Date) == from && period.Max(row => row.Date) == to && period.First().Rule == rule);
        if (same is not null)
            return SpecialDayChange.None(same.Key);

        var dates = Dates(from, to);
        RequireFree(dates, exceptPeriod: null);
        RequireRoom(dates.Count, exceptPeriod: null);

        var periodId = Guid.NewGuid();
        var added = dates.Select(date => new SpecialDay(_tenantId, periodId, date, rule, revision: 1)).ToList();
        _rows.AddRange(added);
        return new SpecialDayChange(added, [], [], periodId, null, null, new SpecialDayPeriodSnapshot(from, to, rule));
    }

    /// <summary>
    /// Moves a period or gives it another rule. Under way, it keeps its first date and can end as early as yesterday;
    /// a new rule applies from today, under a new period, and the dates before keep the old one.
    /// </summary>
    public SpecialDayChange Change(Guid periodId, DateOnly from, DateOnly to, SpecialDayRule rule)
    {
        var (rows, before) = Period(periodId);
        var upcoming = rows.Where(row => row.Date >= _today).ToList();
        var inProgress = before.From < _today;
        if (inProgress)
        {
            if (from != before.From)
                throw new DomainException("Ya empezó: no puedes cambiar desde qué día.");
            if (to < _today.AddDays(-1))
                throw new DomainException("La fecha final no puede ser antes de ayer: los días que ya pasaron se quedan como estaban.");
        }
        else
        {
            RequireOrder(from, to);
            RequireFromToday(from);
        }
        RequireWithinAYear(to);

        var ruleChanged = rule != before.Rule;
        var split = inProgress && ruleChanged && to >= _today;
        RequireLength(split ? _today : from, to);
        if (from == before.From && to == before.To && !ruleChanged)
            return SpecialDayChange.None(periodId);

        var target = to >= _today ? Dates(from > _today ? from : _today, to) : [];
        RequireFree(target, exceptPeriod: periodId);
        RequireRoom(target.Count, exceptPeriod: periodId);

        var newPeriodId = split ? Guid.NewGuid() : periodId;
        var revision = upcoming.Max(row => row.Revision) + 1;
        var modified = new List<SpecialDay>();
        var removed = new List<SpecialDay>();
        foreach (var row in upcoming)
        {
            if (target.Contains(row.Date))
            {
                row.Apply(newPeriodId, rule, revision);
                modified.Add(row);
            }
            else
            {
                removed.Add(row);
            }
        }
        var added = target.Except(upcoming.Select(row => row.Date))
            .Select(date => new SpecialDay(_tenantId, newPeriodId, date, rule, revision))
            .ToList();

        _rows.RemoveAll(removed.Contains);
        _rows.AddRange(added);
        var after = target.Count == 0
            ? before with { To = _today.AddDays(-1) }
            : new SpecialDayPeriodSnapshot(split ? _today : from, to, rule);
        return new SpecialDayChange(added, modified, removed, periodId, split ? newPeriodId : null, before, after);
    }

    /// <summary>Removes a period; one under way ends yesterday instead.</summary>
    public SpecialDayChange Remove(Guid periodId)
    {
        var (rows, before) = Period(periodId);
        var removed = rows.Where(row => row.Date >= _today).ToList();

        _rows.RemoveAll(removed.Contains);
        var after = before.From < _today ? before with { To = _today.AddDays(-1) } : null;
        return new SpecialDayChange([], [], removed, periodId, null, before, after);
    }

    // Every row of the period, past ones included, and the period as the owner sees it. One that already ended is history.
    private (List<SpecialDay> Rows, SpecialDayPeriodSnapshot Snapshot) Period(Guid periodId)
    {
        var rows = _rows.Where(row => row.PeriodId == periodId).OrderBy(row => row.Date).ToList();
        if (rows.Count == 0)
            throw new EntityNotFoundException(nameof(SpecialDay), periodId);
        if (rows[^1].Date < _today)
            throw new DomainException("Ese día especial ya pasó: se queda en el historial.");

        // The rows from today on share the rule: a new rule for a period under way moves them to another period.
        var rule = rows.First(row => row.Date >= _today).Rule;
        return (rows, new SpecialDayPeriodSnapshot(rows[0].Date, rows[^1].Date, rule));
    }

    private static List<DateOnly> Dates(DateOnly from, DateOnly to)
        => Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays).ToList();

    private static void RequireOrder(DateOnly from, DateOnly to)
    {
        if (to < from)
            throw new DomainException("La fecha final no puede ser antes de la inicial.");
    }

    private void RequireFromToday(DateOnly from)
    {
        if (from < _today)
            throw new DomainException("Elige una fecha de hoy en adelante.");
    }

    private void RequireWithinAYear(DateOnly to)
    {
        if (to > _today.AddDays(SpecialDay.MaxDaysAhead))
            throw new DomainException("Solo puedes marcar días especiales hasta un año adelante.");
    }

    private static void RequireLength(DateOnly from, DateOnly to)
    {
        if (to.DayNumber - from.DayNumber + 1 > SpecialDay.MaxDaysPerPeriod)
            throw new DomainException($"Un día especial dura como máximo {SpecialDay.MaxDaysPerPeriod} días.");
    }

    // A date belongs to one period: the owner changes or removes the other one first.
    private void RequireFree(IReadOnlyCollection<DateOnly> dates, Guid? exceptPeriod)
    {
        var taken = Upcoming
            .Where(row => row.PeriodId != exceptPeriod && dates.Contains(row.Date))
            .OrderBy(row => row.Date)
            .FirstOrDefault();
        if (taken is not null)
            throw new DomainException($"El {SpanishCalendar.LongDate(taken.Date)} ya tiene un día especial: cámbialo o quítalo primero.");
    }

    private void RequireRoom(int dates, Guid? exceptPeriod)
    {
        if (Upcoming.Count(row => row.PeriodId != exceptPeriod) + dates > SpecialDay.MaxUpcomingDates)
            throw new DomainException(
                $"Puedes tener hasta {SpecialDay.MaxUpcomingDates} fechas especiales próximas. Quita alguna antes de agregar otra.");
    }
}
