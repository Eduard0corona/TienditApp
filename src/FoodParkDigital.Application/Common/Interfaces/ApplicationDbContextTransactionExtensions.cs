using FoodParkDigital.Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Common.Interfaces;

public static class ApplicationDbContextTransactionExtensions
{
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
    /// method returns, once the data is committed.
    /// </remarks>
    public static Task<TResult> ExecuteInResilientTransactionAsync<TResult>(
        this IApplicationDbContext context,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
        where TResult : Result
    {
        var strategy = context.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(
            async ct =>
            {
                // A retried attempt must not replay entities tracked by the rolled-back one.
                context.ChangeTracker.Clear();

                await using var transaction = await context.Database.BeginTransactionAsync(ct);
                var result = await operation(ct);

                if (result.IsSuccess)
                {
                    await transaction.CommitAsync(ct);
                }
                else
                {
                    await transaction.RollbackAsync(ct);
                    context.ChangeTracker.Clear();
                }

                return result;
            },
            cancellationToken);
    }
}
