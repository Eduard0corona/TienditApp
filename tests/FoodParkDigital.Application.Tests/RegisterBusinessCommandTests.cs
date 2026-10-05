using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

public class RegisterBusinessCommandTests
{
    // The messages Identity gives in production (in Spanish).
    private static readonly IdentityErrorDescriber IdentityErrors = new SpanishIdentityErrorDescriber();

    [Fact]
    public async Task RegisterBusiness_ShouldNotPersistAnything_WhenPasswordViolatesIdentityPolicy()
    {
        await using var host = await SqliteTestHost.CreateAsync("TENANT_ADMIN");

        // Passes the command validator (6+ chars) but not Identity's digit/uppercase rules.
        var result = await RegisterAsync(host, ValidCommand() with { Password = "abcdef" });

        Assert.True(result.IsFailure);
        Assert.Contains(IdentityErrors.PasswordRequiresDigit().Description, result.Error);
        Assert.Contains(IdentityErrors.PasswordRequiresUpper().Description, result.Error);
        await AssertNothingPersistedAsync(host);
        Assert.Empty(host.Email.ConfirmationRecipients);
        Assert.Empty(host.Sms.VerificationRecipients);
    }

    [Fact]
    public async Task RegisterBusiness_ShouldRollBackTenantRows_WhenIdentityRejectsTheOwner()
    {
        await using var host = await SqliteTestHost.CreateAsync("TENANT_ADMIN");

        // The password is valid, but Identity's default user name rules reject the "ñ". That check
        // only runs inside CreateAsync, after the tenant rows were already written.
        var result = await RegisterAsync(host, ValidCommand() with { Email = "dueño@demo.com" });

        Assert.True(result.IsFailure);
        Assert.Contains(IdentityErrors.InvalidUserName("dueño@demo.com").Description, result.Error);
        await AssertNothingPersistedAsync(host);
        Assert.Empty(host.Email.ConfirmationRecipients);
        Assert.Empty(host.Sms.VerificationRecipients);
    }

    [Fact]
    public async Task RegisterBusiness_ShouldCommitTenantOwnerAndSession_WithRetryingExecutionStrategy()
    {
        await using var host = await SqliteTestHost.CreateAsync("TENANT_ADMIN");

        var result = await RegisterAsync(host, ValidCommand());

        Assert.True(result.IsSuccess, result.Error);
        await AssertSingleRegistrationAsync(host, result.Value);
    }

    [Fact]
    public async Task RegisterBusiness_ShouldRetryTheWholeRegistration_WhenCommitFailsTransiently()
    {
        await using var host = await SqliteTestHost.CreateAsync("TENANT_ADMIN");
        host.CommitFaults.FailNext();

        var result = await RegisterAsync(host, ValidCommand());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, host.CommitFaults.Remaining);
        await AssertSingleRegistrationAsync(host, result.Value);
    }

    private static Task<Result<AuthResponse>> RegisterAsync(SqliteTestHost host, RegisterBusinessCommand command)
        => host.HandleAsync<RegisterBusinessCommandHandler, Result<AuthResponse>>(
            handler => handler.Handle(command, CancellationToken.None));

    private static RegisterBusinessCommand ValidCommand() => new()
    {
        Email = "owner@demo.com",
        Password = "Secreto1",
        FirstName = "Ana",
        LastName = "Dueña",
        PhoneNumber = "+525511112222",
        BusinessName = "Taquería Demo",
        BusinessType = BusinessType.Food
    };

    private static async Task AssertNothingPersistedAsync(SqliteTestHost host)
    {
        var counts = await host.QueryAsync(async db => new
        {
            Tenants = await db.Tenants.IgnoreQueryFilters().CountAsync(),
            Settings = await db.TenantSettings.IgnoreQueryFilters().CountAsync(),
            Customers = await db.Customers.IgnoreQueryFilters().CountAsync(),
            Locations = await db.InventoryLocations.IgnoreQueryFilters().CountAsync(),
            Users = await db.Users.CountAsync(),
            UserRoles = await db.UserRoles.CountAsync(),
            Memberships = await db.TenantUsers.IgnoreQueryFilters().CountAsync(),
            RefreshTokens = await db.RefreshTokens.CountAsync()
        });

        Assert.Equal(
            new { Tenants = 0, Settings = 0, Customers = 0, Locations = 0, Users = 0, UserRoles = 0, Memberships = 0, RefreshTokens = 0 },
            counts);
    }

    private static async Task AssertSingleRegistrationAsync(SqliteTestHost host, AuthResponse response)
    {
        var stored = await host.QueryAsync(async db => new
        {
            Tenants = await db.Tenants.IgnoreQueryFilters().ToListAsync(),
            Settings = await db.TenantSettings.IgnoreQueryFilters().ToListAsync(),
            Customers = await db.Customers.IgnoreQueryFilters().ToListAsync(),
            Locations = await db.InventoryLocations.IgnoreQueryFilters().ToListAsync(),
            Users = await db.Users.ToListAsync(),
            Roles = await db.UserRoles
                .Join(db.Roles, userRole => userRole.RoleId, role => role.Id, (userRole, role) => new { userRole.UserId, role.Name })
                .ToListAsync(),
            Memberships = await db.TenantUsers.IgnoreQueryFilters().ToListAsync(),
            RefreshTokens = await db.RefreshTokens.ToListAsync()
        });

        var tenant = Assert.Single(stored.Tenants);
        Assert.Equal("taqueria-demo", tenant.Slug);
        var settings = Assert.Single(stored.Settings);
        Assert.Equal(tenant.Id, settings.TenantId);
        // A new business starts with prices that include IVA and is offered the guided setup.
        Assert.True(settings.PricesIncludeTax);
        Assert.Equal(16m, settings.DefaultTaxRate);
        Assert.Null(settings.OnboardingCompletedAt);
        var publicGeneral = Assert.Single(stored.Customers);
        Assert.Equal(tenant.Id, publicGeneral.TenantId);
        Assert.True(publicGeneral.IsPublicGeneral);
        var location = Assert.Single(stored.Locations);
        Assert.Equal(tenant.Id, location.TenantId);
        Assert.True(location.IsDefault);

        var owner = Assert.Single(stored.Users);
        Assert.Equal(response.User!.Id, owner.Id);
        Assert.Equal(tenant.Id, owner.DefaultTenantId);
        Assert.Equal(new { UserId = owner.Id, Name = (string?)"TENANT_ADMIN" }, Assert.Single(stored.Roles));

        var membership = Assert.Single(stored.Memberships);
        Assert.Equal(tenant.Id, membership.TenantId);
        Assert.Equal(owner.Id, membership.UserId);
        Assert.Equal(TenantUserRole.Owner, membership.Role);
        Assert.Equal(TenantUserStatus.Active, membership.Status);

        var session = Assert.Single(stored.RefreshTokens);
        Assert.Equal(owner.Id, session.UserId);
        Assert.Equal(RefreshToken.HashOf(response.RefreshToken!), session.TokenHash);
        Assert.NotEqual(Guid.Empty, session.SessionId);

        // Notifications go out once, after the commit, even when the transaction was retried.
        Assert.Equal("owner@demo.com", Assert.Single(host.Email.ConfirmationRecipients));
        Assert.Equal("+525511112222", Assert.Single(host.Sms.VerificationRecipients));
    }
}
