using System.Globalization;
using System.Text.Json;
using FoodParkDigital.Domain.Tenants;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FoodParkDigital.Infrastructure.Persistence.Configurations;

/// <summary>
/// How the hours of a business are stored: one JSON column in [TenantSettings], a list of
/// {"day":1,"opens":"09:00","closes":"14:00"} with the day as <see cref="DayOfWeek"/> (Sunday 0). Nothing queries
/// it; reading it back goes through the same rules as saving it.
/// </summary>
internal static class OpeningHoursStorage
{
    /// <summary>Fourteen openings (two a day) take about 600 characters.</summary>
    public const int MaxLength = 1000;

    private const string TimeFormat = "HH\\:mm";

    // EF Core does not hand nulls to a converter (a business without hours stays NULL); the checks only say so to
    // the compiler, for a property that takes them.
    public static readonly ValueConverter<OpeningHours?, string?> Converter = new(
        hours => hours == null ? null : ToStored(hours),
        stored => stored == null ? null : FromStored(stored));

    internal static string ToStored(OpeningHours hours) => JsonSerializer.Serialize(
        hours.Intervals.Select(interval => new StoredInterval(
            (int)interval.Day,
            interval.Opens.ToString(TimeFormat, CultureInfo.InvariantCulture),
            interval.Closes.ToString(TimeFormat, CultureInfo.InvariantCulture))));

    internal static OpeningHours FromStored(string stored) => OpeningHours.Create(
        (JsonSerializer.Deserialize<List<StoredInterval>>(stored) ?? [])
            .Select(interval => new OpeningInterval(
                (DayOfWeek)interval.Day,
                TimeOnly.ParseExact(interval.Opens, TimeFormat, CultureInfo.InvariantCulture),
                TimeOnly.ParseExact(interval.Closes, TimeFormat, CultureInfo.InvariantCulture))));

    private sealed record StoredInterval(
        [property: System.Text.Json.Serialization.JsonPropertyName("day")] int Day,
        [property: System.Text.Json.Serialization.JsonPropertyName("opens")] string Opens,
        [property: System.Text.Json.Serialization.JsonPropertyName("closes")] string Closes);
}
