using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

public class ResendPhoneConfirmationCommandTests
{
    private const string TypedPhone = "55 1234 5678";

    [Fact]
    public async Task Resend_ShouldSendANewCode_ToTheAccountOfTheUserId()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");
        var customer = await CustomerSeed.UnconfirmedCustomerAsync(host, "ana@demo.com", TypedPhone);

        var result = await ResendAsync(host, new ResendPhoneConfirmationCommand { UserId = customer.UserId });

        Assert.True(result.IsSuccess);
        // The phone is stored as typed at registration: a search by E.164 number would not find it.
        Assert.Equal([TypedPhone], host.Sms.VerificationRecipients);
        // Queued, not sent during the request: waiting for the provider would take longer only for real accounts.
        Assert.Equal(host.Sms.Codes, host.Sms.Queued);
    }

    [Fact]
    public async Task Resend_ShouldSendOneCodePerMinute()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");
        var customer = await CustomerSeed.UnconfirmedCustomerAsync(host, "ana@demo.com", TypedPhone);

        var first = await ResendAsync(host, new ResendPhoneConfirmationCommand { UserId = customer.UserId });
        var second = await ResendAsync(host, new ResendPhoneConfirmationCommand { UserId = customer.UserId });

        // The same answer for the second request, so it says nothing about the account either.
        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Single(host.Sms.Codes);
    }

    [Fact]
    public async Task Resend_ShouldNotSend_WhenThePhoneIsAlreadyConfirmed()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");
        var customer = await CustomerSeed.UnconfirmedCustomerAsync(host, "ana@demo.com", TypedPhone);
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == customer.UserId);
            user.PhoneNumberConfirmed = true;
            await db.SaveChangesAsync();
        });

        var result = await ResendAsync(host, new ResendPhoneConfirmationCommand { UserId = customer.UserId });

        Assert.True(result.IsSuccess);
        Assert.Empty(host.Sms.VerificationRecipients);
    }

    [Fact]
    public async Task Resend_ShouldAnswerSuccess_WithoutRevealingUnknownAccounts()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");

        var result = await ResendAsync(host, new ResendPhoneConfirmationCommand { UserId = Guid.NewGuid() });

        Assert.True(result.IsSuccess);
        Assert.Empty(host.Sms.VerificationRecipients);
    }

    [Fact]
    public void Validator_ShouldRequireTheAccountOrAnE164Phone()
    {
        var validator = new ResendPhoneConfirmationCommandValidator();

        Assert.False(validator.Validate(new ResendPhoneConfirmationCommand()).IsValid);
        Assert.False(validator.Validate(new ResendPhoneConfirmationCommand { PhoneNumber = TypedPhone }).IsValid);
        Assert.True(validator.Validate(new ResendPhoneConfirmationCommand { PhoneNumber = "+525512345678" }).IsValid);
        Assert.True(validator.Validate(new ResendPhoneConfirmationCommand { UserId = Guid.NewGuid() }).IsValid);
    }

    private static Task<Result> ResendAsync(SqliteTestHost host, ResendPhoneConfirmationCommand command)
        => host.HandleAsync<ResendPhoneConfirmationCommandHandler, Result>(
            handler => handler.Handle(command, CancellationToken.None));
}
