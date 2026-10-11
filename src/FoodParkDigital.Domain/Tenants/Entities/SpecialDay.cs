using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tenants.Entities;

/// <summary>
/// One date the business closes or opens with other hours (Configuración › Negocio › Días especiales). The dates added
/// together with one rule share a <see cref="PeriodId"/>: «del 21 de diciembre al 4 de enero, cerrado». Only
/// <see cref="SpecialDayCalendar"/> creates and changes them, and the dates that already passed never change.
/// </summary>
public class SpecialDay : AuditableEntity, ITenantScoped
{
    public const int MaxDaysPerPeriod = 92;
    /// <summary>How far ahead a date can be marked, counted from the business's today.</summary>
    public const int MaxDaysAhead = 366;
    /// <summary>Dates from today on a business can have at once.</summary>
    public const int MaxUpcomingDates = 120;
    /// <summary>How far back the settings show the days that passed.</summary>
    public const int PastDaysShown = 90;

    private SpecialDay() { }

    internal SpecialDay(Guid tenantId, Guid periodId, DateOnly date, SpecialDayRule rule, int revision)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("El día especial necesita su negocio.");

        TenantId = tenantId;
        Date = date;
        Apply(periodId, rule, revision);
    }

    public Guid TenantId { get; private set; }
    public Guid PeriodId { get; private set; }
    public DateOnly Date { get; private set; }
    public SpecialDayKind Kind { get; private set; }
    public TimeOnly? FirstOpens { get; private set; }
    public TimeOnly? FirstCloses { get; private set; }
    public TimeOnly? SecondOpens { get; private set; }
    public TimeOnly? SecondCloses { get; private set; }
    public string? Reason { get; private set; }

    /// <summary>Goes up by one with every change; the dates of a period from today on share it.</summary>
    public int Revision { get; private set; }

    public SpecialDayRule Rule => SpecialDayRule.FromStorage(Kind, FirstOpens, FirstCloses, SecondOpens, SecondCloses, Reason);

    internal void Apply(Guid periodId, SpecialDayRule rule, int revision)
    {
        var first = rule.Openings.Count > 0 ? rule.Openings[0] : null;
        var second = rule.Openings.Count > 1 ? rule.Openings[1] : null;

        PeriodId = periodId;
        Kind = rule.Kind;
        FirstOpens = first?.Opens;
        FirstCloses = first?.Closes;
        SecondOpens = second?.Opens;
        SecondCloses = second?.Closes;
        Reason = rule.Reason;
        Revision = revision;
    }
}
