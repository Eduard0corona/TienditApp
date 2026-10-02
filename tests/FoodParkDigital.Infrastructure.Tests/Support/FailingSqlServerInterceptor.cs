using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>
/// Stands in for SQL Server: connections open without a server and every command fails with the
/// exception from <paramref name="failure"/>, so EF Core runs its real error handling and logging.
/// </summary>
public sealed class FailingSqlServerInterceptor(Func<Exception> failure) : IDbConnectionInterceptor, IDbCommandInterceptor
{
    public InterceptionResult ConnectionOpening(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        => InterceptionResult.Suppress();

    public ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(InterceptionResult.Suppress());

    public InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        => throw failure();

    public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
        => throw failure();
}
