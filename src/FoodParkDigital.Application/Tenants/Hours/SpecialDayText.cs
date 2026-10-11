using System.Globalization;
using FoodParkDigital.Application.Tenants.DTOs;
using FoodParkDigital.Domain.Tenants;

namespace FoodParkDigital.Application.Tenants.Hours;

/// <summary>Special days for the activity log, the same in any culture.</summary>
public static class SpecialDayText
{
    /// <summary>"2026-12-24 SpecialHours 09:00-18:00 (Nochebuena)" or "2026-12-21..2027-01-04 Closed (Vacaciones)".</summary>
    public static string Describe(SpecialDayPeriodSnapshot period)
    {
        var text = period.From == period.To ? Date(period.From) : $"{Date(period.From)}..{Date(period.To)}";
        text += $" {period.Rule.Kind}";
        if (period.Rule.Openings.Count > 0)
        {
            text += " " + string.Join(", ", period.Rule.Openings.Select(opening =>
                $"{opening.Opens.ToString(OpeningHoursMappings.TimeFormat, CultureInfo.InvariantCulture)}-" +
                opening.Closes.ToString(OpeningHoursMappings.TimeFormat, CultureInfo.InvariantCulture)));
        }
        if (period.Rule.Reason is { } reason)
            text += $" ({reason})";
        return text;
    }

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
