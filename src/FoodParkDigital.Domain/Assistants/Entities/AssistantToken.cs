using FoodParkDigital.Domain.Assistants.Enums;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Assistants.Entities;

/// <summary>
/// A token of an assistant's connection (Fase 32E, ADR 0097): the access one opens the account's tools for an hour; the
/// refresh one gets new tokens for 30 days and each use replaces it, so a replaced one presented again was copied. Only its
/// SHA-256 is stored, and it never reaches a log.
/// </summary>
public class AssistantToken : BaseEntity
{
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);

    /// <summary>How long a token is kept once it expired: then the daily cleanup deletes it.</summary>
    public static readonly TimeSpan KeptAfterExpiry = TimeSpan.FromDays(7);

    public const string AlreadyRotatedMessage = "El token ya se cambió por otro.";

    private AssistantToken() { }

    public Guid ConnectionId { get; private set; }
    public AssistantTokenKind Kind { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When a refresh token was replaced by the next one.</summary>
    public DateTimeOffset? RotatedAt { get; private set; }

    public static AssistantToken Issue(Guid connectionId, AssistantTokenKind kind, string token, DateTimeOffset now)
    {
        if (connectionId == Guid.Empty)
            throw new DomainException("El token necesita una conexión.");
        if (!Enum.IsDefined(kind))
            throw new DomainException("El tipo de token no es válido.");
        if (!AssistantSecrets.IsWellFormed(token))
            throw new DomainException("El token no es válido.");

        return new AssistantToken
        {
            ConnectionId = connectionId,
            Kind = kind,
            TokenHash = AssistantSecrets.HashOf(token),
            IssuedAt = now,
            ExpiresAt = now + (kind == AssistantTokenKind.Access ? AccessLifetime : RefreshLifetime)
        };
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>In its time and, for a refresh token, not replaced yet.</summary>
    public bool IsUsable(DateTimeOffset now) => !IsExpired(now) && RotatedAt is null;

    /// <summary>A refresh token gave its place to the next one.</summary>
    public void Rotate(DateTimeOffset now)
    {
        if (Kind != AssistantTokenKind.Refresh)
            throw new DomainException("Solo un token de actualización se cambia por otro.");
        if (RotatedAt is not null)
            throw new DomainException(AlreadyRotatedMessage);
        RotatedAt = now;
    }
}
