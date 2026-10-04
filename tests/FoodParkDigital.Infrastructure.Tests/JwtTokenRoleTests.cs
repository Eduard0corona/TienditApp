using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Infrastructure.Identity;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Extensions.Options;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>Inside a business the token carries the role the person has there, not the one of their account.</summary>
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
            NewUser(accountRole), new TenantTokenContext(Guid.NewGuid(), membershipRole)));

        Assert.Equal(expected, Assert.Single(token.Claims, c => c.Type is ClaimTypes.Role or "role").Value);
        Assert.Equal(membershipRole, token.Claims.Single(c => c.Type == "tenant_role").Value);
    }

    [Fact]
    public void RoleClaim_WithoutABusiness_ShouldBeTheAccountRole()
    {
        var token = Read(NewService().GenerateAccessToken(NewUser(UserRole.TenantAdmin)));

        Assert.Equal("TENANT_ADMIN", Assert.Single(token.Claims, c => c.Type is ClaimTypes.Role or "role").Value);
    }

    private static JwtTokenService NewService() => new(
        Options.Create(new JwtSettings
        {
            Secret = new string('k', 64),
            Issuer = "tienditapp-tests",
            Audience = "tienditapp-tests"
        }),
        InMemoryApplicationDbContext.Create());

    private static ApplicationUser NewUser(UserRole role) => new()
    {
        Id = Guid.NewGuid(),
        Email = "persona@negocio.test",
        UserName = "persona@negocio.test",
        FirstName = "Persona",
        LastName = "Demo",
        Role = role
    };

    private static JwtSecurityToken Read(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);
}
