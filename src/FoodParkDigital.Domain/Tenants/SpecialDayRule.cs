using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tenants;

/// <summary>
/// One opening that starts on a date, in the business's own time. As in the weekly hours, a closing at or before the
/// opening ends the next day, and the same time for both means 24 hours.
/// </summary>
public sealed record DailyOpening(TimeOnly Opens, TimeOnly Closes)
{
    /// <summary>The whole day, 00:00 to 00:00 of the next one.</summary>
    public static readonly DailyOpening AllDay = new(TimeOnly.MinValue, TimeOnly.MinValue);

    public bool EndsNextDay => Closes <= Opens;

    public bool IsAllDay => Opens == Closes;
}

/// <summary>
/// What a special day does to its date: the business stays closed, or opens with up to two other openings. The reason
/// («Navidad») is public: the menu and the WhatsApp replies show it.
/// </summary>
public sealed class SpecialDayRule : ValueObject
{
    public const int MaxOpenings = 2;
    public const int MaxReasonLength = 40;

    private SpecialDayRule(SpecialDayKind kind, IReadOnlyList<DailyOpening> openings, string? reason)
    {
        Kind = kind;
        Openings = openings;
        Reason = reason;
    }

    public SpecialDayKind Kind { get; }

    /// <summary>By opening time; none on a closed day.</summary>
    public IReadOnlyList<DailyOpening> Openings { get; }

    public string? Reason { get; }

    public static SpecialDayRule Closed(string? reason) => Create(SpecialDayKind.Closed, [], reason);

    public static SpecialDayRule Hours(IEnumerable<DailyOpening> openings, string? reason)
        => Create(SpecialDayKind.SpecialHours, openings, reason);

    /// <summary>The rule as the owner sent it: the openings must make sense together, and only with special hours.</summary>
    public static SpecialDayRule Create(SpecialDayKind kind, IEnumerable<DailyOpening> openings, string? reason)
    {
        if (!Enum.IsDefined(kind))
            throw new DomainException("El tipo de día especial no existe.");

        var sorted = openings.OrderBy(opening => opening.Opens).ToArray();
        if (kind == SpecialDayKind.Closed && sorted.Length > 0)
            throw new DomainException("Un día cerrado no lleva horario.");
        if (kind == SpecialDayKind.SpecialHours)
        {
            if (sorted.Length == 0)
                throw new DomainException("Escribe al menos un horario para ese día.");
            if (sorted.Length > MaxOpenings)
                throw new DomainException("Un día especial admite como máximo dos horarios.");
            if (sorted.Length > 1)
            {
                if (sorted.Any(opening => opening.IsAllDay))
                    throw new DomainException("Abierto las 24 horas no lleva otro horario ese día.");
                // Only the last opening may end the next day; the second one starts once the first closes.
                if (sorted[0].EndsNextDay)
                    throw new DomainException("Solo el último horario de ese día puede terminar al día siguiente.");
                if (sorted[1].Opens < sorted[0].Closes)
                    throw new DomainException("Los dos horarios de ese día se enciman.");
            }
        }

        return new SpecialDayRule(kind, sorted, NormalizeReason(reason));
    }

    /// <summary>
    /// A stored day as it is: the database checks its shape, so reading a row never throws (a public menu that cannot
    /// read a row would fail for every customer).
    /// </summary>
    internal static SpecialDayRule FromStorage(
        SpecialDayKind kind, TimeOnly? firstOpens, TimeOnly? firstCloses, TimeOnly? secondOpens, TimeOnly? secondCloses, string? reason)
    {
        var openings = new List<DailyOpening>(MaxOpenings);
        if (firstOpens is { } opens && firstCloses is { } closes)
            openings.Add(new DailyOpening(opens, closes));
        if (secondOpens is { } laterOpens && secondCloses is { } laterCloses)
            openings.Add(new DailyOpening(laterOpens, laterCloses));
        return new SpecialDayRule(kind, openings, reason);
    }

    // A single line of at most 40 characters: it goes in the chip and the notices of the menu and in WhatsApp.
    private static string? NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return null;
        var trimmed = reason.Trim();
        if (trimmed.Length > MaxReasonLength || trimmed.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029'))
            throw new DomainException("El motivo tiene máximo 40 caracteres y va en una sola línea.");
        return trimmed;
    }

    // Flat and never empty, so equal rules compare and hash alike (a closed day without reason included).
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Kind;
        yield return Reason ?? string.Empty;
        for (var index = 0; index < MaxOpenings; index++)
        {
            var opening = index < Openings.Count ? Openings[index] : null;
            yield return opening?.Opens;
            yield return opening?.Closes;
        }
    }
}
