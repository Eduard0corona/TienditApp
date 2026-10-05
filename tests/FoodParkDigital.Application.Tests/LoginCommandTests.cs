using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Identity.Services;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The login tells nothing about an account to whoever does not know its password: an email without an account, an
/// inactive one, a locked one and a wrong password get the same answer. A locked account answers so even to the right
/// password (guessing it while locked must not tell which guess is right); only an unconfirmed account is told, and
/// only with its password.
/// </summary>
public class LoginCommandTests
{
    private const string Email = "ana@demo.com";
    private const string Password = "Secreto12";
    private const string Wrong = "Secreto99";

    [Fact]
    public async Task UnknownEmail_InactiveAccount_AndWrongPassword_ShouldGetTheSameAnswer()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedAccountAsync(host, confirmed: true);
        await SeedAccountAsync(host, confirmed: true, email: "baja@demo.com", active: false);

        var unknown = await LoginAsync(host, "nadie@demo.com", Password);
        var inactive = await LoginAsync(host, "baja@demo.com", Password);
        var wrong = await LoginAsync(host, Email, Wrong);

        Assert.All([unknown, inactive, wrong], result =>
            Assert.Equal(LoginCommandHandler.InvalidCredentialsMessage, result.Error));
    }

    [Fact]
    public async Task UnconfirmedAccount_ShouldBeToldOnly_ToWhoeverKnowsItsPassword()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedAccountAsync(host, confirmed: false);

        var wrong = await LoginAsync(host, Email, Wrong);
        var right = await LoginAsync(host, Email, Password);

        Assert.Equal(LoginCommandHandler.InvalidCredentialsMessage, wrong.Error);
        Assert.Equal(LoginCommandHandler.NotConfirmedMessage, right.Error);
    }

    [Fact]
    public async Task LockedAccount_ShouldAnswerLikeAWrongPassword_EvenToTheRightOne()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var userId = await SeedAccountAsync(host, confirmed: true);

        var attempts = new List<Result<AuthResponse>>();
        for (var i = 0; i < 5; i++)
            attempts.Add(await LoginAsync(host, Email, Wrong));
        var wrongWhileLocked = await LoginAsync(host, Email, Wrong);
        var rightWhileLocked = await LoginAsync(host, Email, Password);

        // Even the attempt that locks it answers as a wrong password, and the answer says that the account locks.
        Assert.All(attempts, attempt => Assert.Equal(LoginCommandHandler.InvalidCredentialsMessage, attempt.Error));
        Assert.Equal(LoginCommandHandler.InvalidCredentialsMessage, wrongWhileLocked.Error);
        Assert.Equal(LoginCommandHandler.InvalidCredentialsMessage, rightWhileLocked.Error);
        // The right password does not lift the lock: it lasts its time, or until the password is recovered.
        Assert.True(await host.InScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            return await users.IsLockedOutAsync((await users.FindByIdAsync(userId.ToString()))!);
        }));
    }

    [Fact]
    public async Task RightPassword_ShouldSignIn()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedAccountAsync(host, confirmed: true);

        var result = await LoginAsync(host, Email, Password);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(IssuedSession.AccessToken, result.Value.AccessToken);
    }

    private static Task<Result<AuthResponse>> LoginAsync(SqliteTestHost host, string email, string password) =>
        host.InScopeAsync(services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            return new LoginCommandHandler(
                    users,
                    new UserManagerSignInService(users),
                    services.GetRequiredService<ApplicationDbContext>(),
                    new NoTwoFactorChallenge(),
                    new IssuedSession())
                .Handle(new LoginCommand { Email = email, Password = password }, CancellationToken.None);
        });

    private static Task<Guid> SeedAccountAsync(
        SqliteTestHost host,
        bool confirmed,
        string email = Email,
        bool active = true) => host.InScopeAsync(async services =>
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = confirmed,
            FirstName = "Ana",
            LastName = "Demo",
            Role = UserRole.TenantAdmin,
            IsActive = active
        };
        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, created.ToErrorMessage());
        return user.Id;
    });

    /// <summary>Like SignInManager.CheckPasswordSignInAsync with lockout, over the host's UserManager.</summary>
    private sealed class UserManagerSignInService(UserManager<ApplicationUser> users) : ISignInService
    {
        public async Task<SignInResultDto> CheckPasswordSignInAsync(Guid userId, string password, bool lockoutOnFailure)
        {
            var user = await users.FindByIdAsync(userId.ToString());
            if (user is null)
                return new SignInResultDto(false);
            if (await users.IsLockedOutAsync(user))
                return new SignInResultDto(false, IsLockedOut: true);
            if (await users.CheckPasswordAsync(user, password))
            {
                await users.ResetAccessFailedCountAsync(user);
                return new SignInResultDto(true);
            }

            if (lockoutOnFailure)
                await users.AccessFailedAsync(user);
            return new SignInResultDto(false, IsLockedOut: await users.IsLockedOutAsync(user));
        }
    }

    private sealed class NoTwoFactorChallenge : ITwoFactorLoginChallengeService
    {
        public Task<string?> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The account has no two-step verification.");

        public Task<ApplicationUser?> ValidateAsync(string? token, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The account has no two-step verification.");

        public Task<bool> ConsumeAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The account has no two-step verification.");
    }

    private sealed class IssuedSession : IAuthSessionIssuer
    {
        public const string AccessToken = "token-de-la-sesion";

        public Task<AuthResponse> IssueAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(new AuthResponse { AccessToken = AccessToken });
    }
}
