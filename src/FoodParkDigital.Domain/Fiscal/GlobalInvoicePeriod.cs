using System.Globalization;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal.Enums;

namespace FoodParkDigital.Domain.Fiscal;

/// <summary>
/// The days a global invoice covers (Fase 28B, ADR 0046) and how its InformacionGlobal names them: c_Periodicidad, c_Meses
/// (01 to 12, or 13 to 18 for two months) and the year. A period never mixes two months (or two pairs of months): the sales
/// of each month are declared in that month, so a week that crosses into the next month ends on its last day and the rest
/// is the next period.
/// </summary>
public sealed record GlobalInvoicePeriod
{
    private GlobalInvoicePeriod(GlobalInvoicePeriodicity periodicity, DateOnly from, DateOnly to)
    {
        Periodicity = periodicity;
        From = from;
        To = to;
    }

    public GlobalInvoicePeriodicity Periodicity { get; }

    /// <summary>The first day of the period, in the business's calendar.</summary>
    public DateOnly From { get; }

    /// <summary>The last day of the period, in the business's calendar.</summary>
    public DateOnly To { get; }

    /// <summary>c_Periodicidad: «01» to «05».</summary>
    public string PeriodicityCode => ((int)Periodicity).ToString("00", CultureInfo.InvariantCulture);

    /// <summary>c_Meses: the month (01 to 12), or the pair of months (13 to 18) of a bimonthly period.</summary>
    public string Months => Periodicity == GlobalInvoicePeriodicity.Bimonthly
        ? (12 + (From.Month + 1) / 2).ToString("00", CultureInfo.InvariantCulture)
        : From.Month.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>The year of the period (Año).</summary>
    public int Year => From.Year;

    /// <summary>The period of that periodicity that holds <paramref name="day"/>.</summary>
    public static GlobalInvoicePeriod Containing(GlobalInvoicePeriodicity periodicity, DateOnly day)
    {
        var monthStart = new DateOnly(day.Year, day.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        return periodicity switch
        {
            GlobalInvoicePeriodicity.Daily => new(periodicity, day, day),
            GlobalInvoicePeriodicity.Weekly => Week(day, monthStart, monthEnd),
            GlobalInvoicePeriodicity.Fortnightly => day.Day <= 15
                ? new(periodicity, monthStart, monthStart.AddDays(14))
                : new(periodicity, monthStart.AddDays(15), monthEnd),
            GlobalInvoicePeriodicity.Monthly => new(periodicity, monthStart, monthEnd),
            GlobalInvoicePeriodicity.Bimonthly => Bimester(day),
            _ => throw new DomainException("Elige si la factura global es diaria, semanal, quincenal, mensual o bimestral.")
        };
    }

    /// <summary>Whether the period is over by <paramref name="today"/> (its last day is behind): only then is it invoiced.</summary>
    public bool EndedBy(DateOnly today) => To < today;

    /// <summary>Whether <paramref name="day"/> falls in the period.</summary>
    public bool Holds(DateOnly day) => day >= From && day <= To;

    /// <summary>Monday to Sunday, cut at the ends of the month.</summary>
    private static GlobalInvoicePeriod Week(DateOnly day, DateOnly monthStart, DateOnly monthEnd)
    {
        var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        var sunday = monday.AddDays(6);
        return new(
            GlobalInvoicePeriodicity.Weekly,
            monday < monthStart ? monthStart : monday,
            sunday > monthEnd ? monthEnd : sunday);
    }

    private static GlobalInvoicePeriod Bimester(DateOnly day)
    {
        var firstMonth = day.Month % 2 == 1 ? day.Month : day.Month - 1;
        var from = new DateOnly(day.Year, firstMonth, 1);
        return new(GlobalInvoicePeriodicity.Bimonthly, from, from.AddMonths(2).AddDays(-1));
    }
}
