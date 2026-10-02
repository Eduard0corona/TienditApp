using System.Text;
using Microsoft.Data.SqlClient;

namespace FoodParkDigital.Infrastructure.Services;

/// <summary>
/// Logged in place of a database exception chain outside Development (see
/// <see cref="LogRedaction.RedactDatabaseMessages"/>). It is never thrown: <see cref="ToString"/> renders
/// the original chain like <see cref="Exception.ToString"/> does, with every type, SQL Server error number
/// and stack trace, but without the messages, which can carry row values.
/// </summary>
public sealed class RedactedDatabaseException : Exception
{
    private const string OmittedMessage = "[message omitted outside Development]";

    private readonly string _details;

    internal RedactedDatabaseException(Exception exception)
        : base(LogRedaction.GetSqlErrorNumber(exception) is { } number
            ? $"Database error {number}; exception messages are omitted outside Development."
            : "Database error; exception messages are omitted outside Development.")
    {
        var builder = new StringBuilder();
        Describe(builder, exception);
        _details = builder.ToString();
    }

    public override string ToString() => _details;

    private static void Describe(StringBuilder builder, Exception exception)
    {
        builder.Append(exception.GetType().FullName);
        if (exception is SqlException sqlException)
            builder.Append(" (SQL error ").Append(sqlException.Number).Append(')');
        builder.Append(": ").Append(OmittedMessage);

        foreach (var inner in LogRedaction.InnerExceptionsOf(exception))
        {
            builder.Append(" ---> ");
            Describe(builder, inner);
            builder.AppendLine().Append("   --- End of inner exception stack trace ---");
        }

        if (exception.StackTrace is { } stackTrace)
            builder.AppendLine().Append(stackTrace);
    }
}
