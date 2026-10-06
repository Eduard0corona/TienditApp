using System.Security.Cryptography;
using System.Text;
using FoodParkDigital.Domain.Common;
using FoodParkDigital.Domain.Identity.Enums;

namespace FoodParkDigital.Domain.Identity.Entities;

/// <summary>
/// What renews the access token of a session (one sign-in on one device). It rotates on every use: the session keeps
/// its id and the token presented is replaced, so a replaced token presented again was copied and ends the session.
/// Only its SHA-256 is stored: a copy of the table opens no session.
/// </summary>
public class RefreshToken : BaseEntity
{
    private RefreshToken() { }

    public RefreshToken(Guid userId, Guid sessionId, string value, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("El token no puede estar vacío.", nameof(value));
        if (sessionId == Guid.Empty)
            throw new ArgumentException("El token debe pertenecer a una sesión.", nameof(sessionId));

        UserId = userId;
        SessionId = sessionId;
        TokenHash = HashOf(value);
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }

    /// <summary>The sign-in this token renews: every rotation keeps it, and the access tokens of the session carry it.</summary>
    public Guid SessionId { get; private set; }

    /// <summary>
    /// SHA-256 of the value, in hexadecimal. Null only in rows a rollback of the hashing left without it: they open no
    /// session.
    /// </summary>
    public string? TokenHash { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public RefreshTokenRevocationReason? RevocationReason { get; private set; }

    public ApplicationUser User { get; private set; } = null!;

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt is not null;
    public bool IsActive => !IsRevoked && !IsExpired;

    /// <summary>Replaced by the next token of its session: presenting it again is a reuse.</summary>
    public bool WasRotated => RevocationReason == RefreshTokenRevocationReason.Rotated;

    /// <summary>Stops the token; a token already revoked keeps its first reason.</summary>
    public void Revoke(RefreshTokenRevocationReason reason, DateTimeOffset now)
    {
        if (RevokedAt is not null)
            return;

        RevokedAt = now;
        RevocationReason = reason;
    }

    public static string HashOf(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
