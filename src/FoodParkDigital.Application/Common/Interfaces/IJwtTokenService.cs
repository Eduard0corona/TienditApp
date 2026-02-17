using System.Security.Claims;
using FoodParkDigital.Domain.Identity.Entities;

namespace FoodParkDigital.Application.Common.Interfaces;

public interface IJwtTokenService
{
    string GenerateAccessToken(ApplicationUser user, TenantTokenContext? tenantContext = null);
    RefreshToken GenerateRefreshToken(Guid userId);
    Task<bool> ValidateRefreshTokenAsync(Guid userId, string refreshToken);
    Task RevokeRefreshTokenAsync(string refreshToken, string? replacedByToken = null);
    Task RevokeAllUserRefreshTokensAsync(Guid userId);
    Task<Guid?> GetUserIdFromRefreshTokenAsync(string refreshToken);
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}

public record TenantTokenContext(Guid TenantId, string TenantRole);
