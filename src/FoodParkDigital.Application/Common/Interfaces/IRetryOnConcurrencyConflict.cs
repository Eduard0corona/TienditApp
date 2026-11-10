namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// A transactional command that someone else may change the same rows of at the same time (Fase 31G): two employees
/// moving the same order, the customer cancelling while the business accepts. When its save finds a row changed since it
/// read it, <see cref="Behaviors.TransactionBehavior{TRequest,TResponse}"/> undoes the whole transaction, the folio it
/// took included, and runs it again from an empty change tracker, up to three times. A retry inside the handler would
/// not be safe: with MARS SQL Server keeps no savepoints, so it would repeat what the failed save already wrote.
/// </summary>
public interface IRetryOnConcurrencyConflict : ITransactionalRequest
{
    /// <summary>
    /// What the command answers when the rows keep changing after the last attempt; null lets the conflict through
    /// (409). Only for a command that answers a plain <see cref="Models.Result"/>.
    /// </summary>
    string? ConcurrencyConflictMessage => null;
}
