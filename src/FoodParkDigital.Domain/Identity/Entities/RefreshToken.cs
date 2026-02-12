using FoodParkDigital.Domain.Common;

namespace FoodParkDigital.Domain.Identity.Entities;

public class RefreshToken : BaseEntity
{
    private RefreshToken() { }

    public RefreshToken(Guid userId, string token, DateTimeOffset expiresAt)
    {
        UserId = userId;
        Token = token;
        ExpiresAt = expiresAt;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid UserId { get; private set; }
    public string Token { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ReplacedByToken { get; private set; }

    public ApplicationUser User { get; private set; } = null!;

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt is not null;
    public bool IsActive => !IsRevoked && !IsExpired;

    public void Revoke(string? replacedByToken = null)
    {
        RevokedAt = DateTimeOffset.UtcNow;
        ReplacedByToken = replacedByToken;
    }
}
