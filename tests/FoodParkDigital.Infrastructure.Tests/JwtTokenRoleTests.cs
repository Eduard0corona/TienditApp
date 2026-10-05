using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Identity;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// Inside a business the token carries the role the person has there, not the one of their account; every access token
/// carries its session, and a refresh token is kept only as its hash.
/// </summary>
public class JwtTokenRoleTests
{
    [Theory]
    [InlineData(UserRole.TenantAdmin, "OWNER", "TENANT_ADMIN")]
    [InlineData(UserRole.TenantAdmin, "STAFF", "STAFF")] // Owns another business, works here.
    [InlineData(UserRole.Staff, "ADMIN", "TENANT_ADMIN")]
    [InlineData(UserRole.Kitchen, "STAFF", "KITCHEN")]
    [InlineData(UserRole.SuperAdmin, "STAFF", "SUPER_ADMIN")]
    public void RoleClaim_ShouldBeTheRoleInTheBusiness(UserRole accountRole, string membershipRole, string expected)
    {
        var token = Read(NewService().GenerateAccessToken(
            NewUser(accountRole), Guid.NewGuid(), new TenantTokenContext(Guid.NewGuid(), membershipRole)));

        Assert.Equal(expected, Assert.Single(token.Claims, c => c.Type is ClaimTypes.Role or "role").Value);
        Assert.Equal(membershipRole, token.Claims.Single(c => c.Type == "tenant_role").Value);
    }

    [Fact]
    public void RoleClaim_WithoutABusiness_ShouldBeTheAccountRole()
    {
        var token = Read(NewService().GenerateAccessToken(NewUser(UserRole.TenantAdmin), Guid.NewGuid()));

        Assert.Equal("TENANT_ADMIN", Assert.Single(token.Claims, c => c.Type is ClaimTypes.Role or "role").Value);
    }

    [Fact]
    public void AccessToken_ShouldCarryItsSession_AndOnlyAFingerprintOfTheSecurityStamp()
    {
        var user = NewUser(UserRole.Staff);
        var sessionId = Guid.NewGuid();

        var token = Read(NewService().GenerateAccessToken(user, sessionId));

        Assert.Equal(sessionId.ToString(), token.Claims.Single(c => c.Type == SessionClaims.SessionId).Value);
        var stamp = token.Claims.Single(c => c.Type == SessionClaims.SecurityStamp).Value;
        Assert.Equal(SessionClaims.StampFingerprint(user.SecurityStamp), stamp);
        // The stamp seeds the SMS codes: the readable token must not carry it.
        Assert.DoesNotContain(token.Claims, c => c.Value.Contains(user.SecurityStamp!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RefreshToken_ShouldBeStoredOnlyAsItsHash()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var issued = NewService().GenerateRefreshToken(userId, sessionId);

        Assert.Equal(64, Convert.FromBase64String(issued.Value).Length);
        Assert.Equal(RefreshToken.HashOf(issued.Value), issued.Token.TokenHash);
        Assert.Equal(userId, issued.Token.UserId);
        Assert.Equal(sessionId, issued.Token.SessionId);
        Assert.Equal(Now, issued.Token.CreatedAt);
        Assert.Equal(Now.AddDays(7), issued.Token.ExpiresAt);
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static JwtTokenService NewService() => new(
        Options.Create(new JwtSettings
        {
            Secret = new string('k', 64),
            Issuer = "tienditapp-tests",
            Audience = "tienditapp-tests",
            RefreshTokenExpirationDays = 7
        }),
        new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private static ApplicationUser NewUser(UserRole role) => new()
    {
        Id = Guid.NewGuid(),
        Email = "persona@negocio.test",
        UserName = "persona@negocio.test",
        FirstName = "Persona",
        LastName = "Demo",
        Role = role,
        SecurityStamp = Guid.NewGuid().ToString("N").ToUpperInvariant()
    };

    private static JwtSecurityToken Read(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);
}
