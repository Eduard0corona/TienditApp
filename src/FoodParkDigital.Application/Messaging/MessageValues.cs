namespace FoodParkDigital.Application.Messaging;

/// <summary>
/// Values for template placeholders. Dates are written in Spanish here instead of through a culture, so the
/// text is the same on any server (invariant globalization included).
/// </summary>
public static class MessageValues
{
    public const string DefaultCustomerName = "cliente";
    private const string FallbackTimeZone = "America/Mexico_City";

    private static readonly string[] Days = ["domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"];

    private static readonly string[] Months =
    [
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"
    ];

    /// <summary>The first word of the name, for a greeting; "cliente" when there is none.</summary>
    public static string FirstName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return DefaultCustomerName;

        var first = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return first.Length > 40 ? first[..40] : first;
    }

    public static DateTimeOffset ToLocal(DateTimeOffset instant, string? timeZoneId)
        => TimeZoneInfo.ConvertTime(instant, FindZone(timeZoneId));

    /// <summary>"lunes 5 de octubre".</summary>
    public static string FormatDate(DateTimeOffset local)
        => $"{Days[(int)local.DayOfWeek]} {local.Day} de {Months[local.Month - 1]}";

    /// <summary>"4:30 p. m.".</summary>
    public static string FormatTime(DateTimeOffset local)
    {
        var hour = local.Hour % 12 == 0 ? 12 : local.Hour % 12;
        return $"{hour}:{local.Minute:00} {(local.Hour < 12 ? "a. m." : "p. m.")}";
    }

    private static TimeZoneInfo FindZone(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone))
            return zone;
        if (TimeZoneInfo.TryFindSystemTimeZoneById(FallbackTimeZone, out var mexico))
            return mexico;

        // Central Mexico has no daylight saving time since 2022.
        return TimeZoneInfo.CreateCustomTimeZone("Mexico-Central", TimeSpan.FromHours(-6), "Centro de México", "CST");
    }
}
