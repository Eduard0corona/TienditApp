using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Signing up sends a link by email and opens no session. The link activates the account and only then does the
/// password sign in. Opening it again still succeeds; a wrong token, an unknown account and a malformed link get the
/// same answer and confirm nothing.
/// </summary>
public class EmailConfirmationTests
{
    private const string Email = "owner@demo.com";
    private const string Password = "Secreto12";

    [Fact]
    public async Task SignedUpOwner_ShouldSignIn_OnlyAfterOpeningTheEmailedLink()
    {
        await using var host = await SqliteTestHost.CreateAsync("TENANT_ADMIN");
        var userId = await RegisterBusinessAsync(host);

        var beforeConfirming = await LoginCommandTests.LoginAsync(host, Email, Password);
        Assert.Equal(LoginCommandHandler.NotConfirmedMessage, beforeConfirming.Error);

        var link = Assert.Single(host.Email.Confirmations);
        Assert.Equal((Email, userId.ToString()), (link.Email, link.UserId));
        var confirmed = await ConfirmAsync(host, link.UserId, link.Token);

        Assert.True(confirmed.IsSuccess, confirmed.Error);
        Assert.True(await IsEmailConfirmedAsync(host, userId));
        var afterConfirming = await LoginCommandTests.LoginAsync(host, Email, Password);
        Assert.True(afterConfirming.IsSuccess, afterConfirming.Error);
    }

    [Fact]
    public async Task OpeningTheLinkAgain_ShouldStillSucceed()
    {
        await using var host = await SqliteTestHost.CreateAsync("TENANT_ADMIN");
        var userId = await RegisterBusinessAsync(host);
        var link = Assert.Single(host.Email.Confirmations);

        var first = await ConfirmAsync(host, link.UserId, link.Token);
        var again = await ConfirmAsync(host, link.UserId, link.Token);

        Assert.True(first.IsSuccess, first.Error);
        Assert.True(again.IsSuccess, again.Error);
        Assert.True(await IsEmailConfirmedAsync(host, userId));
    }

    [Fact]
    public async Task WrongLinks_ShouldGetOneAnswer_AndLeaveTheAccountInactive()
    {
        await using var host = await SqliteTestHost.CreateAsync("TENANT_ADMIN");
        var userId = await RegisterBusinessAsync(host);
        var link = Assert.Single(host.Email.Confirmations);

        var wrongToken = await ConfirmAsync(host, link.UserId, "token-equivocado");
        var noToken = await ConfirmAsync(host, link.UserId, "");
        var unknownAccount = await ConfirmAsync(host, Guid.NewGuid().ToString(), link.Token);
        var malformedId = await ConfirmAsync(host, "no-es-un-id", link.Token);

        Assert.All([wrongToken, noToken, unknownAccount, malformedId], result =>
            Assert.Equal(ConfirmEmailCommandHandler.InvalidLinkMessage, result.Error));
        Assert.False(await IsEmailConfirmedAsync(host, userId));
    }

    private static async Task<Guid> RegisterBusinessAsync(SqliteTestHost host)
    {
        var result = await host.HandleAsync<RegisterBusinessCommandHandler, Result<Guid>>(handler => handler.Handle(
            new RegisterBusinessCommand
            {
                Email = Email,
                Password = Password,
                FirstName = "Ana",
                LastName = "Dueña",
                PhoneNumber = "+525511112222",
                BusinessName = "Taquería Demo",
                BusinessType = BusinessType.Food
            },
            CancellationToken.None));
        Assert.True(result.IsSuccess, result.Error);
        await host.DispatchAccountEmailsAsync();
        return result.Value;
    }

    private static Task<Result> ConfirmAsync(SqliteTestHost host, string userId, string token)
        => host.HandleAsync<ConfirmEmailCommandHandler, Result>(
            handler => handler.Handle(new ConfirmEmailCommand(userId, token), CancellationToken.None));

    private static Task<bool> IsEmailConfirmedAsync(SqliteTestHost host, Guid userId)
        => host.QueryAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.EmailConfirmed).SingleAsync());
}
