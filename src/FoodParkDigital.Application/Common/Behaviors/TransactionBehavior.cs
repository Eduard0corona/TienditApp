using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Common.Behaviors;

/// <summary>
/// Runs an <see cref="ITransactionalRequest"/> as one transaction through the execution strategy: it commits when the
/// handler answers with a successful <see cref="Result"/> and rolls back on a failed one or an exception; a transient
/// failure runs the handler again from an empty change tracker. An <see cref="IRetryOnConcurrencyConflict"/> is also run
/// again, in a new transaction, when its save finds rows changed meanwhile (Fase 31G). Registered last, so authorization
/// and validation run before the transaction opens. The constraints keep it out of every other request.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ITransactionalRequest
    where TResponse : Result
{
    /// <summary>How many times a command that retries on a concurrency conflict runs at most.</summary>
    public const int MaxConcurrencyAttempts = 3;

    private readonly IApplicationDbContext _context;

    public TransactionBehavior(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Sent from inside another unit of work: it is part of that one, and only that one can start it over.
        if (_context.Database.CurrentTransaction is not null)
            return await ApplicationDbContextTransactionExtensions.JoinCallerTransactionAsync(() => next());

        if (request is not IRetryOnConcurrencyConflict retrying)
            return await _context.ExecuteRequestInTransactionAsync(_ => next(), cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _context.ExecuteRequestInTransactionAsync(_ => next(), cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxConcurrencyAttempts)
            {
                // The transaction was rolled back when it was disposed: nothing of this attempt stays, its folio
                // included. The next one reads the rows again.
                _context.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException) when (retrying.ConcurrencyConflictMessage is { } message
                && typeof(TResponse) == typeof(Result))
            {
                _context.ChangeTracker.Clear();
                return (TResponse)Result.Failure(message);
            }
        }
    }
}
