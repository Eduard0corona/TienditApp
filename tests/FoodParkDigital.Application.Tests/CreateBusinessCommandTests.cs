using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Tenants.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

/// <summary>An account opens another business and owns it, starting like a business from the sign-up.</summary>
public class CreateBusinessCommandTests
{
    [Fact]
    public async Task Owner_ShouldOpenAnotherBusiness_WhileWorkingInTheFirstOne()
    {
        await using var host = await SqliteTestHost.CreateAsync("TENANT_ADMIN");
        var registration = await host.HandleAsync<RegisterBusinessCommandHandler, Result<AuthResponse>>(handler => handler.Handle(new RegisterBusinessCommand
        {
            Email = "owner@demo.com",
            Password = "Secreto1",
            FirstName = "Ana",
            LastName = "Dueña",
            PhoneNumber = "+525511112222",
            BusinessName = "Taquería Demo",
            BusinessType = BusinessType.Food
        }, CancellationToken.None));
        Assert.True(registration.IsSuccess, registration.Error);
        var userId = registration.Value!.User!.Id;
        var firstTenantId = registration.Value.User.TenantId!.Value;
        // The request runs under the business the person is in.
        host.SetTenant(firstTenantId);

        var result = await CreateAsync(host, userId, "Lavandería Burbujas", BusinessType.Service);

        Assert.True(result.IsSuccess, result.Error);
        var created = result.Value!;
        Assert.Equal("Owner", created.Role);
        Assert.Equal("lavanderia-burbujas", created.Slug);
        var stored = await host.QueryAsync(async db => new
        {
            Tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == created.TenantId),
            Settings = await db.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == created.TenantId),
            Branches = await db.Branches.IgnoreQueryFilters().Where(b => b.TenantId == created.TenantId).ToListAsync(),
            Locations = await db.InventoryLocations.IgnoreQueryFilters().Where(l => l.TenantId == created.TenantId).ToListAsync(),
            PublicCustomers = await db.Customers.IgnoreQueryFilters().CountAsync(c => c.TenantId == created.TenantId && c.IsPublicGeneral),
            Memberships = await db.TenantUsers.IgnoreQueryFilters().Where(m => m.UserId == userId).ToListAsync(),
            Audit = await db.AuditLogs.IgnoreQueryFilters().Where(a => a.TenantId == created.TenantId).ToListAsync()
        });
        Assert.Equal(BusinessType.Service, stored.Tenant.BusinessType);
        Assert.Equal("owner@demo.com", stored.Tenant.ContactEmail);
        Assert.True(stored.Settings.PricesIncludeTax);
        Assert.Equal("MXN", stored.Settings.Currency);
        Assert.True(Assert.Single(stored.Branches).IsDefault);
        Assert.True(Assert.Single(stored.Locations).IsDefault);
        Assert.Equal(1, stored.PublicCustomers);
        // Owner of both, each membership in its own business.
        Assert.Equal(2, stored.Memberships.Count);
        Assert.All(stored.Memberships, m => Assert.Equal(TenantUserRole.Owner, m.Role));
        Assert.Contains(stored.Memberships, m => m.TenantId == firstTenantId);
        Assert.Equal("BusinessOpenedByExistingAccount", Assert.Single(stored.Audit).Action);
    }

    [Fact]
    public async Task CustomerAccount_ShouldNotOpenABusiness()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var customerId = await AddUserAsync(host, UserRole.Customer);

        var result = await CreateAsync(host, customerId, "Mi tiendita", BusinessType.Retail);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, await host.QueryAsync(db => db.Tenants.IgnoreQueryFilters().CountAsync()));
    }

    [Fact]
    public async Task Account_ShouldNotOwnMoreBusinessesThanTheLimit()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var userId = await AddUserAsync(host, UserRole.TenantAdmin);
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<IApplicationDbContext>();
            for (var i = 0; i < CreateBusinessCommandHandler.MaxOwnedBusinesses; i++)
            {
                var tenant = new Tenant($"Negocio {i}", $"negocio-{i}", BusinessType.Retail, "owner@demo.com");
                db.Tenants.Add(tenant);
                db.TenantUsers.Add(new TenantUser(tenant.Id, userId, TenantUserRole.Owner));
            }
            await db.SaveChangesAsync(CancellationToken.None);
        });

        var result = await CreateAsync(host, userId, "Uno más", BusinessType.Retail);

        Assert.False(result.IsSuccess);
        Assert.Contains($"{CreateBusinessCommandHandler.MaxOwnedBusinesses} negocios", result.Error);
    }

    private static Task<Result<MyTenantDto>> CreateAsync(SqliteTestHost host, Guid userId, string name, BusinessType type)
        => host.InScopeAsync(services => new CreateBusinessCommandHandler(
                services.GetRequiredService<IApplicationDbContext>(),
                new SignedInUser(userId),
                services.GetRequiredService<IDateTimeProvider>())
            .Handle(new CreateBusinessCommand { BusinessName = name, BusinessType = type }, CancellationToken.None));

    private static Task<Guid> AddUserAsync(SqliteTestHost host, UserRole role)
        => host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<IApplicationDbContext>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = "persona@demo.com",
                UserName = "persona@demo.com",
                NormalizedEmail = "PERSONA@DEMO.COM",
                NormalizedUserName = "PERSONA@DEMO.COM",
                SecurityStamp = Guid.NewGuid().ToString(),
                FirstName = "Persona",
                LastName = "Demo",
                Role = role,
                IsActive = true
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(CancellationToken.None);
            return user.Id;
        });

    private sealed class SignedInUser(Guid userId) : ICurrentUserService
    {
        public string? UserId { get; } = userId.ToString();
        public string? UserName => "persona@demo.com";
        public string? Role => null;
        public string? TenantRole => null;
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }
}
