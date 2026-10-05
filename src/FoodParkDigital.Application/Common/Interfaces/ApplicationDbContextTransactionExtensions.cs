using FoodParkDigital.Application.Common.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FoodParkDigital.Application.Common.Interfaces;

public static class ApplicationDbContextTransactionExtensions
{
    // The transaction opened for the request running on this flow; null inside one that joined its caller's.
    private static readonly AsyncLocal<IDbContextTransaction?> OpenedForRequest = new();

    /// <summary>
    /// Runs <paramref name="operation"/> as a single database transaction through the configured
    /// execution strategy, so a transient failure retries the whole unit of work instead of failing
    /// because retrying strategies reject user-initiated transactions. The transaction commits only
    /// when the operation returns a successful <see cref="Result"/>; a failed result or an exception
    /// rolls it back.
    /// </summary>
    /// <remarks>
    /// Every attempt starts with an empty change tracker, so the operation must load or create
    /// everything it persists. External side effects (email, SMS, notifications) belong after this
    /// method returns, once the data is committed; the realtime events of what it saved are published
    /// then too.
    /// </remarks>
    public static Task<TResult> ExecuteInResilientTransactionAsync<TResult>(
        this IApplicationDbContext context,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
        where TResult : Result
        => ExecuteAsync(context, operation, emptyTrackerOnFirstAttempt: true, cancellationToken);

    /// <summary>
    /// The transaction of a whole request (<see cref="Behaviors.TransactionBehavior{TRequest,TResponse}"/>): like
    /// <see cref="ExecuteInResilientTransactionAsync{TResult}"/>, but the first attempt keeps what the pipeline already
    /// loaded; only a retry starts with an empty change tracker.
    /// </summary>
    internal static Task<TResult> ExecuteRequestInTransactionAsync<TResult>(
        this IApplicationDbContext context,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
        where TResult : Result
        => ExecuteAsync(context, operation, emptyTrackerOnFirstAttempt: false, cancellationToken);

    /// <summary>
    /// Undoes what the current request wrote (its whole transaction, when it runs in one) and stops tracking it. For a
    /// request that ends up answering with data it did not write, such as the copy a concurrent request stored first.
    /// A request that joined its caller's transaction cannot discard it: that would undo the caller's work too.
    /// </summary>
    public static async Task DiscardUnitOfWorkAsync(this IApplicationDbContext context, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is { } transaction)
        {
            if (!ReferenceEquals(transaction, OpenedForRequest.Value))
                throw new InvalidOperationException("Only the request that opened the transaction can discard it.");

            await context.Database.RollbackTransactionAsync(cancellationToken);
        }

        context.ChangeTracker.Clear();
    }

    /// <summary>Runs a request inside its caller's transaction, which it did not open (and so cannot discard).</summary>
    internal static async Task<TResult> JoinCallerTransactionAsync<TResult>(Func<Task<TResult>> operation)
    {
        OpenedForRequest.Value = null;
        return await operation();
    }

    private static async Task<TResult> ExecuteAsync<TResult>(
        IApplicationDbContext context,
        Func<CancellationToken, Task<TResult>> operation,
        bool emptyTrackerOnFirstAttempt,
        CancellationToken cancellationToken)
        where TResult : Result
    {
        var strategy = context.Database.CreateExecutionStrategy();
        var attempts = 0;
        IDbContextTransaction? committed = null;

        var result = await strategy.ExecuteAsync(
            async ct =>
            {
                // A retried attempt must not replay entities tracked by the rolled-back one.
                if (attempts++ > 0 || emptyTrackerOnFirstAttempt)
                    context.ChangeTracker.Clear();

                await using var transaction = await context.Database.BeginTransactionAsync(ct);
                // Null when the provider has no transactions (the in-memory one of some tests).
                var current = context.Database.CurrentTransaction;
                OpenedForRequest.Value = current;
                var result = await operation(ct);

                // The operation ended the transaction itself (DiscardUnitOfWorkAsync): there is nothing left to commit.
                if (!ReferenceEquals(context.Database.CurrentTransaction, current))
                    return result;

                if (result.IsSuccess)
                {
                    await transaction.CommitAsync(ct);
                    committed = transaction;
                }
                else
                {
                    await transaction.RollbackAsync(ct);
                    context.ChangeTracker.Clear();
                }

                return result;
            },
            cancellationToken);

        // Screens refresh once the data is there for them to read.
        if (committed is not null)
            await context.PublishCommittedEventsAsync(committed, cancellationToken);

        return result;
    }
}
