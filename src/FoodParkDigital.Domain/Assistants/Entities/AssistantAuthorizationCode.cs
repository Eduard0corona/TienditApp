using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Assistants.Entities;

/// <summary>
/// What a customer's permission gives an assistant (Fase 32E, ADR 0097): a code of one use, for five minutes, that the
/// assistant exchanges for its tokens. It is tied to everything the request said: the client, its redirect_uri, the
/// code_challenge (PKCE), the resource and the scope, and to the account that allowed it. Only its SHA-256 is stored.
/// </summary>
public class AssistantAuthorizationCode : BaseEntity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public const int MaxResourceLength = 2000;

    public const string AlreadyRedeemedMessage = "El código ya se usó.";
    public const string ExpiredMessage = "El código venció.";

    private AssistantAuthorizationCode() { }

    /// <summary>The client (<see cref="AssistantClient"/>) it was given to.</summary>
    public Guid ClientId { get; private set; }

    /// <summary>The customer account that allowed it.</summary>
    public Guid UserId { get; private set; }

    public string CodeHash { get; private set; } = string.Empty;
    public string RedirectUri { get; private set; } = string.Empty;
    public string CodeChallenge { get; private set; } = string.Empty;

    /// <summary>The server its tokens are for (RFC 8707): the account's MCP server.</summary>
    public string Resource { get; private set; } = string.Empty;

    public string Scope { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RedeemedAt { get; private set; }

    /// <summary>The connection its exchange made: a second exchange of the code ends it (RFC 6749, 4.1.2).</summary>
    public Guid? ConnectionId { get; private set; }

    public static AssistantAuthorizationCode Issue(
        AssistantClient client,
        Guid userId,
        string code,
        string redirectUri,
        string codeChallenge,
        string resource,
        string scope,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (userId == Guid.Empty)
            throw new DomainException("El permiso necesita una cuenta.");
        if (!AssistantSecrets.IsWellFormed(code))
            throw new DomainException("El código no es válido.");
        if (!client.AllowsRedirect(redirectUri))
            throw new DomainException("La dirección de regreso no es de este asistente.");
        if (!AssistantSecrets.IsCodeChallenge(codeChallenge))
            throw new DomainException("El code_challenge no es válido.");
        if (string.IsNullOrWhiteSpace(resource) || resource.Length > MaxResourceLength)
            throw new DomainException("El recurso no es válido.");
        if (scope != AssistantConnection.AccountScope)
            throw new DomainException("El permiso solo es para la cuenta.");

        return new AssistantAuthorizationCode
        {
            ClientId = client.Id,
            UserId = userId,
            CodeHash = AssistantSecrets.HashOf(code),
            RedirectUri = redirectUri,
            CodeChallenge = codeChallenge,
            Resource = resource,
            Scope = scope,
            IssuedAt = now,
            ExpiresAt = now + Lifetime
        };
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>Whether <paramref name="codeVerifier"/> is the PKCE verifier of its challenge.</summary>
    public bool Verifies(string? codeVerifier) => AssistantSecrets.Verifies(CodeChallenge, codeVerifier);

    /// <summary>Whether it was given for <paramref name="resource"/> (RFC 8707): it is exchanged only at that server.</summary>
    public bool IsFor(string resource) => string.Equals(Resource, resource, StringComparison.Ordinal);

    /// <summary>The assistant exchanged it for the tokens of <paramref name="connectionId"/>: once, and in its time.</summary>
    public void Redeem(Guid connectionId, DateTimeOffset now)
    {
        if (RedeemedAt is not null)
            throw new DomainException(AlreadyRedeemedMessage);
        if (IsExpired(now))
            throw new DomainException(ExpiredMessage);
        if (connectionId == Guid.Empty)
            throw new DomainException("La conexión no existe.");

        RedeemedAt = now;
        ConnectionId = connectionId;
    }
}
