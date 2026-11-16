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

    /// <summary>
    /// Where the code that made it went (its client may have registered several): the site Mi cuenta shows is the one the
    /// customer allowed on the permission page.
    /// </summary>
    public string RedirectUri { get; private set; } = string.Empty;

    /// <summary>The server its tokens are for (RFC 8707): they open no other address.</summary>
    public string Resource { get; private set; } = string.Empty;

    public DateTimeOffset GrantedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    /// <summary>
    /// The connection a code makes, with what the code was given for: its account, its assistant, its scope, the address it
    /// went to and the server its tokens are for. The code is redeemed with it.
    /// </summary>
    public static AssistantConnection Grant(AssistantAuthorizationCode code, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.UserId == Guid.Empty)
            throw new DomainException("La conexión necesita una cuenta.");
        if (code.ClientId == Guid.Empty)
            throw new DomainException("La conexión necesita un asistente.");
        if (code.Scope != AccountScope)
            throw new DomainException("El permiso solo es para la cuenta.");
        if (string.IsNullOrEmpty(code.RedirectUri) || string.IsNullOrEmpty(code.Resource))
            throw new DomainException("La conexión necesita la dirección de regreso y el recurso de su código.");

        return new AssistantConnection
        {
            UserId = code.UserId,
            ClientId = code.ClientId,
            Scope = code.Scope,
            RedirectUri = code.RedirectUri,
            Resource = code.Resource,
            GrantedAt = now
        };
    }

    /// <summary>Whether its tokens open <paramref name="resource"/>: only the server they were given for.</summary>
    public bool IsFor(string resource) => string.Equals(Resource, resource, StringComparison.Ordinal);

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
