using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Identity.EventHandlers;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Identity.Events;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The emails of the accounts go out from the outbox, after the answer, with their link created then. Signing up,
/// asking for a new link or recovering the password answers the same whether the email has an account or not: only
/// the owner of the email learns, by email, that it already had one.
/// </summary>
public class AccountEmailTests
{
    private const string Email = "ana@demo.com";
    private const string Password = "Secreto1";

    [Fact]
    public async Task SigningUpAgain_WithAConfirmedAccount_ShouldAnswerTheSame_CreateNothing_AndOnlyTellTheOwner()
    {
        await using var host = await SqliteTestHost.CreateAsync("TENANT_ADMIN", "CUSTOMER");
        var accountId = await SeedAccountAsync(host, emailConfirmed: true);

        var business = await RegisterBusinessAsync(host, Email);
        var customer = await RegisterCustomerAsync(host, Email.ToUpperInvariant());

        Assert.True(business.IsSuccess, business.Error);
        Assert.True(customer.IsSuccess, customer.Error);
        var stored = await host.QueryAsync(async db => new
        {
            Users = await db.Users.CountAsync(),
            Tenants = await db.Tenants.IgnoreQueryFilters().CountAsync(),
            Profiles = await db.CustomerProfiles.CountAsync()
        });
        Assert.Equal(new { Users = 1, Tenants = 0, Profiles = 0 }, stored);

        // Nothing goes out during the requests: each email is asked for, and sent after the answer.
        Assert.Equal(0, host.Email.Sent);
        await host.DispatchAccountEmailsAsync();
        Assert.Equal([Email, Email], host.Email.AlreadyExistsRecipients);
        Assert.Empty(host.Email.Confirmations);
        Assert.Equal(accountId, business.Value);
    }

    [Fact]
    public async Task SigningUpAgain_BeforeConfirming_ShouldSendTheLinkAgain()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");
        var accountId = await SeedAccountAsync(host, emailConfirmed: false);

        var again = await RegisterCustomerAsync(host, Email);

        Assert.True(again.IsSuccess, again.Error);
        await host.DispatchAccountEmailsAsync();
        var link = Assert.Single(host.Email.Confirmations);
        Assert.Equal((Email, accountId.ToString()), (link.Email, link.UserId));
        Assert.Empty(host.Email.AlreadyExistsRecipients);
        var confirmed = await host.HandleAsync<ConfirmEmailCommandHandler, Result>(
            handler => handler.Handle(new ConfirmEmailCommand(link.UserId, link.Token), CancellationToken.None));
        Assert.True(confirmed.IsSuccess, confirmed.Error);
    }

    [Fact]
    public async Task SigningUpAgain_WithADeactivatedAccount_ShouldSendNothing()
    {
        await using var host = await SqliteTestHost.CreateAsync("TENANT_ADMIN");
        await SeedAccountAsync(host, emailConfirmed: true, active: false);

        Assert.True((await RegisterBusinessAsync(host, Email)).IsSuccess);

        await host.DispatchAccountEmailsAsync();
        Assert.Equal(0, host.Email.Sent);
    }

    [Fact]
    public async Task APasswordAgainstThePolicy_ShouldGetTheSameAnswer_WhetherTheEmailHasAnAccountOrNot()
    {
        await using var host = await SqliteTestHost.CreateAsync("TENANT_ADMIN", "CUSTOMER");
        await SeedAccountAsync(host, emailConfirmed: true);

        var taken = await RegisterBusinessAsync(host, Email, password: "abcdef");
        var free = await RegisterBusinessAsync(host, "nuevo@demo.com", password: "abcdef");
        var takenCustomer = await RegisterCustomerAsync(host, Email, password: "abcdef");
        var freeCustomer = await RegisterCustomerAsync(host, "nuevo@demo.com", password: "abcdef");

        Assert.True(taken.IsFailure);
        Assert.Equal(free.Error, taken.Error);
        Assert.True(takenCustomer.IsFailure);
        Assert.Equal(freeCustomer.Error, takenCustomer.Error);
        await host.DispatchAccountEmailsAsync();
        Assert.Equal(0, host.Email.Sent);
    }

    [Fact]
    public async Task PasswordResetByEmail_ShouldGoOutAfterTheAnswer_OnlyToAConfirmedAccount()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var accountId = await SeedAccountAsync(host, emailConfirmed: true);
        await SeedAccountAsync(host, emailConfirmed: false, email: "sin-confirmar@demo.com");

        Assert.True((await ForgotPasswordAsync(host, "nadie@demo.com")).IsSuccess);
        Assert.True((await ForgotPasswordAsync(host, "sin-confirmar@demo.com")).IsSuccess);
        Assert.True((await ForgotPasswordAsync(host, $"  {Email.ToUpperInvariant()} ")).IsSuccess);

        Assert.Equal(0, host.Email.Sent);
        await host.DispatchAccountEmailsAsync();
        var link = Assert.Single(host.Email.PasswordResets);
        Assert.Equal((Email, accountId.ToString()), (link.Email, link.UserId));
        // The link resets the password (the reset command finds the account by its id in text, which SQLite compares
        // with case: Identity is asked directly).
        var reset = await host.InScopeAsync(async services =>
        {
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            return await userManager.ResetPasswordAsync((await userManager.FindByIdAsync(link.UserId))!, link.Token, "NuevaClave2");
        });
        Assert.True(reset.Succeeded, reset.ToErrorMessage());
    }

    [Fact]
    public async Task ANewConfirmationLink_ShouldGoOnlyToAnAccountNotConfirmedYet()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var accountId = await SeedAccountAsync(host, emailConfirmed: false);
        await SeedAccountAsync(host, emailConfirmed: true, email: "confirmada@demo.com");

        Assert.True((await ResendConfirmationAsync(host, "nadie@demo.com")).IsSuccess);
        Assert.True((await ResendConfirmationAsync(host, "confirmada@demo.com")).IsSuccess);
        Assert.True((await ResendConfirmationAsync(host, Email)).IsSuccess);

        await host.DispatchAccountEmailsAsync();
        var link = Assert.Single(host.Email.Confirmations);
        Assert.Equal((Email, accountId.ToString()), (link.Email, link.UserId));
    }

    [Fact]
    public async Task AnEmailAskedForTooLongAgo_ShouldNotBeSent()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var accountId = await SeedAccountAsync(host, emailConfirmed: false);

        await host.InScopeAsync(async services =>
        {
            var now = services.GetRequiredService<FoodParkDigital.Application.Common.Interfaces.IDateTimeProvider>().UtcNow;
            var handler = ActivatorUtilities.CreateInstance<AccountEmailRequestedHandler>(services);
            var late = new OutboxEventContext(
                Guid.NewGuid(), null, null, null, now - AccountEmailRequestedHandler.MaxDelay - TimeSpan.FromMinutes(1), 1);
            await handler.HandleAsync(
                new AccountEmailRequestedEvent(AccountEmailPurpose.EmailConfirmation, UserId: accountId), late, CancellationToken.None);
        });

        Assert.Equal(0, host.Email.Sent);
    }

    private static Task<Guid> SeedAccountAsync(
        SqliteTestHost host,
        bool emailConfirmed,
        bool active = true,
        string email = Email)
        => host.InScopeAsync(async services =>
        {
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = "Ana",
                LastName = "Demo",
                EmailConfirmed = emailConfirmed,
                Role = UserRole.Staff,
                IsActive = active
            };
            var created = await userManager.CreateAsync(user, Password);
            Assert.True(created.Succeeded, created.ToErrorMessage());
            return user.Id;
        });

    private static Task<Result<Guid>> RegisterBusinessAsync(SqliteTestHost host, string email, string password = "Secreto12")
        => host.HandleAsync<RegisterBusinessCommandHandler, Result<Guid>>(handler => handler.Handle(
            new RegisterBusinessCommand
            {
                Email = email,
                Password = password,
                FirstName = "Beto",
                LastName = "Otro",
                PhoneNumber = "+525599998888",
                BusinessName = "Otro negocio",
                BusinessType = BusinessType.Food
            },
            CancellationToken.None));

    private static Task<Result<Guid>> RegisterCustomerAsync(SqliteTestHost host, string email, string password = "Secreto12")
        => host.HandleAsync<RegisterCustomerCommandHandler, Result<Guid>>(handler => handler.Handle(
            new RegisterCustomerCommand
            {
                Email = email,
                Password = password,
                FirstName = "Beto",
                LastName = "Otro",
                PhoneNumber = "5599998888"
            },
            CancellationToken.None));

    private static Task<Result> ForgotPasswordAsync(SqliteTestHost host, string email)
        => host.HandleAsync<ForgotPasswordCommandHandler, Result>(
            handler => handler.Handle(new ForgotPasswordCommand { Email = email }, CancellationToken.None));

    private static Task<Result> ResendConfirmationAsync(SqliteTestHost host, string email)
        => host.HandleAsync<ResendConfirmationEmailCommandHandler, Result>(
            handler => handler.Handle(new ResendConfirmationEmailCommand { Email = email }, CancellationToken.None));
}
