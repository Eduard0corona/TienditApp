using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Services;

/// <summary>
/// Reduces recipients, provider responses and database errors to values that are safe to log outside Development.
/// </summary>
public static class LogRedaction
{
    private const string Hidden = "***";

    /// <summary>Keeps the last four digits of full-length numbers: <c>+525512345678</c> becomes <c>***5678</c>.</summary>
    public static string MaskPhoneNumber(string? phoneNumber)
    {
        var digits = new string((phoneNumber ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length >= 8 ? Hidden + digits[^4..] : Hidden;
    }

    /// <summary>Keeps the initials and the top-level domain: <c>ana@example.com</c> becomes <c>a***@e***.com</c>.</summary>
    public static string MaskEmail(string? email)
    {
        var at = email?.LastIndexOf('@') ?? -1;
        if (at <= 0 || at == email!.Length - 1)
            return Hidden;

        var domain = email[(at + 1)..];
        var dot = domain.LastIndexOf('.');
        var topLevelDomain = dot > 0 ? domain[dot..] : string.Empty;
        return $"{email[0]}{Hidden}@{domain[0]}{Hidden}{topLevelDomain}";
    }

    /// <summary>
    /// Returns the numeric code of a Twilio (<c>{"code": 21211}</c>) or Meta Graph
    /// (<c>{"error": {"code": 131030}}</c>) error response. Their messages can echo phone numbers
    /// or message content, so outside Development only this code is logged.
    /// </summary>
    public static int? GetProviderErrorCode(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var error = document.RootElement;
            if (error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("error", out var graphError)
                && graphError.ValueKind == JsonValueKind.Object)
            {
                error = graphError;
            }

            return error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.Number
                && code.TryGetInt32(out var value)
                    ? value
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns <paramref name="exception"/> unchanged unless its chain contains a database exception.
    /// SQL Server copies row values into those messages (<c>The duplicate key value is (..., +525512345678)</c>,
    /// <c>Truncated value: '...'</c>), so such a chain becomes a <see cref="RedactedDatabaseException"/>
    /// that keeps every exception type, the SQL Server error number and the stack traces, but no message.
    /// </summary>
    public static Exception RedactDatabaseMessages(Exception exception)
        => Flatten(exception).Any(e => e is DbException or DbUpdateException)
            ? new RedactedDatabaseException(exception)
            : exception;

    /// <summary>Returns the number of the first SQL Server error in the chain, such as 2601 for a duplicate key.</summary>
    public static int? GetSqlErrorNumber(Exception exception)
        => Flatten(exception).OfType<SqlException>().Select(e => (int?)e.Number).FirstOrDefault();

    internal static IEnumerable<Exception> InnerExceptionsOf(Exception exception)
    {
        if (exception is AggregateException aggregate)
            return aggregate.InnerExceptions;

        return exception.InnerException is { } inner ? [inner] : [];
    }

    private static IEnumerable<Exception> Flatten(Exception exception)
        => InnerExceptionsOf(exception).SelectMany(Flatten).Prepend(exception);
}
