using FoodParkDigital.Domain.Identity.Entities;

namespace FoodParkDigital.Application.Common.Interfaces;

public interface IJwtTokenService
{
    /// <summary>
    /// The access token of a session. It carries the session and a fingerprint of the account's security stamp
    /// (<see cref="Identity.SessionClaims"/>): it stops working when the session ends or the account's security
    /// changes, even before it expires.
    /// </summary>
    string GenerateAccessToken(ApplicationUser user, Guid sessionId, TenantTokenContext? tenantContext = null);

    /// <summary>A new refresh token of the session. The entity keeps only its hash; the value goes to the client once.</summary>
    IssuedRefreshToken GenerateRefreshToken(Guid userId, Guid sessionId);
}

public sealed record IssuedRefreshToken(RefreshToken Token, string Value);

public record TenantTokenContext(
    Guid TenantId,
    string TenantRole,
    IReadOnlyCollection<string>? Permissions = null);
