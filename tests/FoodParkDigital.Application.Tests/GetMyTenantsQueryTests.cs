using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Identity.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

/// <summary>The business switcher: the businesses of the account, by name, each with its logo in small.</summary>
public class GetMyTenantsQueryTests
{
    [Fact]
    public async Task ShouldListTheBusinessesOfTheAccountByName_WithTheirSmallLogo()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var userId = await AddUserAsync(host, "ana@demo.com");
        var stranger = await AddUserAsync(host, "otra@demo.com");
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<IApplicationDbContext>();
            var taqueria = new Tenant("Taquería Lupita", "lupita", BusinessType.Food, "lupita@demo.com");
            taqueria.Update(taqueria.Name, taqueria.ContactEmail, null, "https://cdn.demo/logos/lupita");
            var lavanderia = new Tenant("Lavandería Burbujas", "burbujas", BusinessType.Service, "burbujas@demo.com");
            var other = new Tenant("Abarrotes Ajenos", "ajenos", BusinessType.Retail, "ajenos@demo.com");
            db.Tenants.AddRange(taqueria, lavanderia, other);
            db.TenantUsers.AddRange(
                new TenantUser(taqueria.Id, userId, TenantUserRole.Owner),
                new TenantUser(lavanderia.Id, userId, TenantUserRole.Staff),
                new TenantUser(other.Id, stranger, TenantUserRole.Owner));
            await db.SaveChangesAsync(CancellationToken.None);
        });

        var businesses = await host.InScopeAsync(services => new GetMyTenantsQueryHandler(
                new SignedInUser(userId),
                services.GetRequiredService<IApplicationDbContext>())
            .Handle(new GetMyTenantsQuery(), CancellationToken.None));

        Assert.Collection(businesses,
            burbujas =>
            {
                Assert.Equal("Lavandería Burbujas", burbujas.Name);
                Assert.Equal("Staff", burbujas.Role);
                Assert.Null(burbujas.LogoUrl);
            },
            lupita =>
            {
                Assert.Equal("Taquería Lupita", lupita.Name);
                Assert.Equal("Owner", lupita.Role);
                Assert.Equal("Active", lupita.Status);
                // The 128 px version the upload made: the switcher shows it at 28 px, twice for sharp screens.
                Assert.Equal("https://cdn.demo/logos/lupita_xs.webp", lupita.LogoUrl);
            });
    }

    private static Task<Guid> AddUserAsync(SqliteTestHost host, string email)
        => host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<IApplicationDbContext>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = email,
                UserName = email,
                NormalizedEmail = email.ToUpperInvariant(),
                NormalizedUserName = email.ToUpperInvariant(),
                SecurityStamp = Guid.NewGuid().ToString(),
                FirstName = "Persona",
                LastName = "Demo",
                Role = UserRole.TenantAdmin,
                IsActive = true
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(CancellationToken.None);
            return user.Id;
        });

    private sealed class SignedInUser(Guid userId) : ICurrentUserService
    {
        public string? UserId { get; } = userId.ToString();
        public string? UserName => "ana@demo.com";
        public string? Role => null;
        public string? TenantRole => null;
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }
}
