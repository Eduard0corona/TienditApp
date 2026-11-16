using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Assistants.Entities;

/// <summary>
/// An assistant a customer connected to their account (Fase 32E, ADR 0097): it reads the account's addresses, orders and
/// favorites and prepares orders the customer sends. It belongs to the account, not to a business. Mi cuenta lists it and
/// disconnects it, which stops its tokens at once; allowing the same assistant again replaces it.
/// </summary>
public class AssistantConnection : AuditableEntity, IAggregateRoot
{
    /// <summary>The only permission there is: the account, as ADR 0097 describes it.</summary>
    public const string AccountScope = "cuenta";

    /// <summary>The assistants one account keeps connected at once.</summary>
    public const int MaxActivePerAccount = 20;

    /// <summary>How often its last use is written: every request would write the table.</summary>
    public static readonly TimeSpan UseRecordedEvery = TimeSpan.FromMinutes(5);

    /// <summary>How long a disconnected one is kept before it is deleted with its tokens.</summary>
    public static readonly TimeSpan KeptAfterRevoked = TimeSpan.FromDays(30);

    public const string TooManyMessage =
        "Tienes 20 asistentes conectados: desconecta uno en Mi cuenta para conectar otro.";

    private AssistantConnection() { }

    public Guid UserId { get; private set; }

    /// <summary>The client (<see cref="AssistantClient"/>) connected.</summary>
    public Guid ClientId { get; private set; }

    public string Scope { get; private set; } = string.Empty;
    public DateTimeOffset GrantedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    public static AssistantConnection Grant(Guid userId, Guid clientId, string scope, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
            throw new DomainException("La conexión necesita una cuenta.");
        if (clientId == Guid.Empty)
            throw new DomainException("La conexión necesita un asistente.");
        if (scope != AccountScope)
            throw new DomainException("El permiso solo es para la cuenta.");

        return new AssistantConnection { UserId = userId, ClientId = clientId, Scope = scope, GrantedAt = now };
    }

    /// <summary>Stops it: its tokens stop opening the account. One already stopped keeps when it was.</summary>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    /// <summary>
    /// Writes that it was used now, at most every <see cref="UseRecordedEvery"/>; whether it changed. A stopped one is not used.
    /// </summary>
    public bool RecordUse(DateTimeOffset now)
    {
        if (!IsActive || (LastUsedAt is { } last && now - last < UseRecordedEvery))
            return false;
        LastUsedAt = now;
        return true;
    }
}
