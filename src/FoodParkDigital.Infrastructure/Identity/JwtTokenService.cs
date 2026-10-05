using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Identity;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FoodParkDigital.Infrastructure.Identity;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _settings;
    private readonly IDateTimeProvider _clock;

    public JwtTokenService(IOptions<JwtSettings> settings, IDateTimeProvider clock)
    {
        _settings = settings.Value;
        _clock = clock;
    }

    public string GenerateAccessToken(ApplicationUser user, Guid sessionId, TenantTokenContext? tenantContext = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // With a business, the role is the one the person has in it (owning another business gives nothing here).
        var role = tenantContext is not null && Enum.TryParse<TenantUserRole>(tenantContext.TenantRole, ignoreCase: true, out var membershipRole)
            ? TenantPermissionCatalog.EffectiveRole(user.Role, membershipRole)
            : user.Role;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email!),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, ToUpperSnakeCase(role.ToString())),
            new(SessionClaims.SessionId, sessionId.ToString()),
            new(SessionClaims.SecurityStamp, SessionClaims.StampFingerprint(user.SecurityStamp))
        };

        if (tenantContext is not null)
        {
            claims.Add(new Claim("tenant_id", tenantContext.TenantId.ToString()));
            claims.Add(new Claim("tenant_role", tenantContext.TenantRole));

            if (tenantContext.Permissions is not null)
            {
                foreach (var permission in tenantContext.Permissions
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    claims.Add(new Claim("perm", permission));
                }
            }
        }

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            // Checked by the bearer handler against the system clock.
            expires: DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public IssuedRefreshToken GenerateRefreshToken(Guid userId, Guid sessionId)
    {
        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var now = _clock.UtcNow;

        return new IssuedRefreshToken(
            new RefreshToken(userId, sessionId, value, now, now.AddDays(_settings.RefreshTokenExpirationDays)),
            value);
    }

    private static string ToUpperSnakeCase(string value) =>
        Regex.Replace(value, "(?<!^)([A-Z])", "_$1").ToUpperInvariant();
}
