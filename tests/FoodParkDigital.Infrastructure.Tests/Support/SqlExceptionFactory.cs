using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// Builds the <see cref="SqlException"/> SQL Server raises. It has no public constructor, so this goes
/// through the internal factory Microsoft.Data.SqlClient itself uses; if a client upgrade changes those
/// internals, the tests that use it fail here instead of passing silently.
/// </summary>
public static class SqlExceptionFactory
{
    /// <summary>
    /// The <see cref="DbUpdateException"/> EF Core throws when SQL Server rejects a save. The inner
    /// <see cref="SqlException"/> is thrown first so that, like a real one, it has a stack trace.
    /// </summary>
    public static DbUpdateException SaveFailure(int number, string message)
    {
        try
        {
            throw Create(number, message);
        }
        catch (SqlException sqlException)
        {
            return new DbUpdateException(
                "An error occurred while saving the entity changes. See the inner exception for details.",
                sqlException);
        }
    }

    public static SqlException Create(int number, string message)
    {
        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection)
            .GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(errors, [CreateError(number, message)]);

        var createException = typeof(SqlException).GetMethod(
            "CreateException",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(SqlErrorCollection), typeof(string)])
            ?? throw new MissingMethodException(nameof(SqlException), "CreateException");

        return (SqlException)createException.Invoke(null, [errors, "17.00.4025"])!;
    }

    private static SqlError CreateError(int number, string message)
    {
        // SqlError(int infoNumber, byte errorState, byte errorClass, string server, string errorMessage,
        //          string procedure, int lineNumber, ...optional values that depend on the client version)
        Type[] leadingParameters =
            [typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int)];

        var constructor = typeof(SqlError)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(c => c.GetParameters().Select(p => p.ParameterType).Take(leadingParameters.Length).SequenceEqual(leadingParameters))
            .OrderBy(c => c.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new MissingMethodException(nameof(SqlError), ".ctor");

        object?[] arguments = [number, (byte)1, (byte)14, "test-server", message, string.Empty, 1];
        var remaining = constructor.GetParameters()
            .Skip(arguments.Length)
            .Select(p => p.HasDefaultValue ? p.DefaultValue : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null);

        return (SqlError)constructor.Invoke([.. arguments, .. remaining]);
    }
}
