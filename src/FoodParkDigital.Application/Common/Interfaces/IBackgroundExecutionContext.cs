namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// The user on whose behalf background work runs, such as the cashier whose sale an outbox handler audits.
/// Outside an HTTP request <see cref="ICurrentUserService"/> answers with these values.
/// </summary>
public interface IBackgroundExecutionContext
{
    string? UserId { get; }
    string? UserName { get; }

    void SetActor(string? userId, string? userName);
}
