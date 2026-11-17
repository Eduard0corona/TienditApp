namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// Puts in line the exchanges of one code or one connection's refresh token (Fase 32E, ADR 0097): inside the transaction of
/// the request, two at once wait for each other, so a code gives tokens once and a refresh token is replaced once.
/// </summary>
public interface IAssistantAuthorizationLock
{
    /// <summary>Holds the lock of <paramref name="key"/> (a code's or a connection's id) until the request's transaction ends.</summary>
    Task AcquireAsync(Guid key, CancellationToken cancellationToken);
}
