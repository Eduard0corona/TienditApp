namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// A command that runs as one database transaction (<see cref="Behaviors.TransactionBehavior{TRequest,TResponse}"/>):
/// what it writes, the folio it takes included, is committed when it answers with a successful result and undone
/// otherwise, so a refused sale gives its folio back. Its response must be a <see cref="Models.Result"/>.
/// </summary>
public interface ITransactionalRequest
{
}
