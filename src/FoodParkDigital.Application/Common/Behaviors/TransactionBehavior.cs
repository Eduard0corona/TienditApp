using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using MediatR;

namespace FoodParkDigital.Application.Common.Behaviors;

/// <summary>
/// Runs an <see cref="ITransactionalRequest"/> as one transaction through the execution strategy: it commits when the
/// handler answers with a successful <see cref="Result"/> and rolls back on a failed one or an exception; a transient
/// failure runs the handler again from an empty change tracker. Registered last, so authorization and validation run
/// before the transaction opens. The constraints keep it out of every other request.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ITransactionalRequest
    where TResponse : Result
{
    private readonly IApplicationDbContext _context;

    public TransactionBehavior(IApplicationDbContext context)
    {
        _context = context;
    }

    public Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Sent from inside another unit of work: it is part of that one.
        if (_context.Database.CurrentTransaction is not null)
            return ApplicationDbContextTransactionExtensions.JoinCallerTransactionAsync(() => next());

        return _context.ExecuteRequestInTransactionAsync(_ => next(), cancellationToken);
    }
}
