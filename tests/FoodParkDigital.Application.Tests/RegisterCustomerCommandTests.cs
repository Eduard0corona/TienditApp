using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Customers.Enums;
using FoodParkDigital.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

public class RegisterCustomerCommandTests
{
    private const string PhoneNormalized = "+525512345678";
    // The messages Identity gives in production (in Spanish).
    private static readonly IdentityErrorDescriber IdentityErrors = new SpanishIdentityErrorDescriber();

    [Fact]
    public async Task RegisterCustomer_ShouldNotPersistAnything_WhenPasswordViolatesIdentityPolicy()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");

        var result = await RegisterAsync(host, ValidCommand() with { Password = "abcdef" });

        Assert.True(result.IsFailure);
        Assert.Contains(IdentityErrors.PasswordRequiresDigit().Description, result.Error);
        await AssertNoAccountPersistedAsync(host);
        await host.DispatchAccountEmailsAsync();
        Assert.Empty(host.Email.ConfirmationRecipients);
        Assert.Empty(host.Sms.VerificationRecipients);
    }

    [Fact]
    public async Task RegisterCustomer_ShouldRollBackUserAndRole_WhenProfileWriteFails()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");

        // A registration racing for the same phone makes the profile insert hit the unique index
        // after the user and role rows were written.
        host.CommandFaults.FailNext(
            command => command.CommandText.Contains("INSERT INTO \"CustomerProfiles\"", StringComparison.Ordinal),
            () => new SqliteException("UNIQUE constraint failed: CustomerProfiles.PhoneNormalized", 19));

        await Assert.ThrowsAsync<DbUpdateException>(() => RegisterAsync(host, ValidCommand()));

        Assert.True(host.CommandFaults.Fired);
        await AssertNoAccountPersistedAsync(host);
        await host.DispatchAccountEmailsAsync();
        Assert.Empty(host.Email.ConfirmationRecipients);
        Assert.Empty(host.Sms.VerificationRecipients);
    }

    [Fact]
    public async Task RegisterCustomer_ShouldCommitAccountWithoutSessionOrGuestOrders_UntilConfirmed()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");
        var guest = await CustomerSeed.GuestOrderAsync(host, "tenant-uno", PhoneNormalized);

        var result = await RegisterAsync(host, ValidCommand());

        Assert.True(result.IsSuccess, result.Error);
        var stored = await host.QueryAsync(async db => new
        {
            Users = await db.Users.ToListAsync(),
            Roles = await db.UserRoles
                .Join(db.Roles, userRole => userRole.RoleId, role => role.Id, (userRole, role) => role.Name)
                .ToListAsync(),
            Profiles = await db.CustomerProfiles.ToListAsync(),
            RefreshTokens = await db.RefreshTokens.ToListAsync(),
            Temp = await db.TemporaryCustomers.IgnoreQueryFilters().SingleAsync(x => x.Id == guest.TemporaryCustomerId),
            Order = await db.Orders.IgnoreQueryFilters().SingleAsync(x => x.Id == guest.OrderId)
        });

        var user = Assert.Single(stored.Users);
        Assert.Equal(result.Value, user.Id);
        Assert.False(user.EmailConfirmed);
        Assert.False(user.PhoneNumberConfirmed);
        // Typed as "55 1234 5678", kept as its profile and its SMS codes use it.
        Assert.Equal(PhoneNormalized, user.PhoneNumber);
        Assert.Equal("CUSTOMER", Assert.Single(stored.Roles));
        var profile = Assert.Single(stored.Profiles);
        Assert.Equal(user.Id, profile.UserId);
        Assert.Equal(PhoneNormalized, profile.PhoneNormalized);
        // Signing up opens no session: the customer signs in once the email is confirmed.
        Assert.Empty(stored.RefreshTokens);

        // Guest orders are linked only after the phone is proven to belong to the user.
        Assert.Equal(TemporaryCustomerStatus.PendingInvited, stored.Temp.Status);
        Assert.Null(stored.Temp.ClaimedUserId);
        Assert.Equal(guest.TemporaryCustomerId, stored.Order.TemporaryCustomerId);
        Assert.Null(stored.Order.CustomerUserId);

        // The confirmation link goes by email; the phone is verified later from the profile, so no SMS code.
        await host.DispatchAccountEmailsAsync();
        Assert.Equal("cliente@demo.com", Assert.Single(host.Email.ConfirmationRecipients));
        Assert.Empty(host.Sms.VerificationRecipients);
    }

    private static Task<Result<Guid>> RegisterAsync(SqliteTestHost host, RegisterCustomerCommand command)
        => host.HandleAsync<RegisterCustomerCommandHandler, Result<Guid>>(
            handler => handler.Handle(command, CancellationToken.None));

    private static RegisterCustomerCommand ValidCommand() => new()
    {
        Email = "cliente@demo.com",
        Password = "Secreto1",
        FirstName = "Ana",
        LastName = "Cliente",
        PhoneNumber = "55 1234 5678"
    };

    private static async Task AssertNoAccountPersistedAsync(SqliteTestHost host)
    {
        var counts = await host.QueryAsync(async db => new
        {
            Users = await db.Users.CountAsync(),
            UserRoles = await db.UserRoles.CountAsync(),
            Profiles = await db.CustomerProfiles.CountAsync(),
            RefreshTokens = await db.RefreshTokens.CountAsync()
        });

        Assert.Equal(new { Users = 0, UserRoles = 0, Profiles = 0, RefreshTokens = 0 }, counts);
    }
}
