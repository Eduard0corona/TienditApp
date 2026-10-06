using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Application.Identity.EventHandlers;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Identity.Events;
using FoodParkDigital.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The password is recovered by SMS only through a phone the person confirmed, the phone only changes with the
/// password and a code to the new number, and wrong passwords or codes lock the account for a while.
/// </summary>
public class AccountRecoveryTests
{
    private const string Email = "ana@demo.com";
    private const string Password = "Secreto1";
    private const string Phone = "+525512345678";

    [Fact]
    public async Task SmsCode_ShouldResetThePassword_AndEndTheOpenSessions()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var userId = await SeedAccountAsync(host, phoneConfirmed: true);
        await host.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.RefreshTokens.Add(host.Jwt.GenerateRefreshToken(userId, Guid.NewGuid()).Token);
            db.RefreshTokens.Add(host.Jwt.GenerateRefreshToken(userId, Guid.NewGuid()).Token);
            await db.SaveChangesAsync();
        });

        Assert.True((await ForgotBySmsAsync(host)).IsSuccess);
        // Nothing is sent during the request: the code goes out after the answer, from the outbox.
        Assert.Empty(host.Sms.Codes);
        await SendRequestedCodesAsync(host);
        var (phone, code) = Assert.Single(host.Sms.Codes);
        var reset = await ResetBySmsAsync(host, code, "NuevaClave2");

        Assert.Equal(Phone, phone);
        Assert.True(reset.IsSuccess, reset.Error);
        Assert.True(await CheckPasswordAsync(host, userId, "NuevaClave2"));
        var sessions = await host.QueryAsync(db => db.RefreshTokens.ToListAsync());
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, token => Assert.Equal(RefreshTokenRevocationReason.PasswordReset, token.RevocationReason));
    }

    [Fact]
    public async Task SmsRecovery_ShouldSendNothing_ToAPhoneThePersonNeverConfirmed()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedAccountAsync(host, phoneConfirmed: false);

        var asked = await ForgotBySmsAsync(host);
        var unknown = await ForgotBySmsAsync(host, "nadie@demo.com");
        await SendRequestedCodesAsync(host);

        // The same answer either way: nothing reveals whether the account or its phone exist.
        Assert.True(asked.IsSuccess);
        Assert.True(unknown.IsSuccess);
        Assert.Empty(host.Sms.Codes);
    }

    [Fact]
    public async Task CodeRequests_ShouldDoTheSameWork_WhetherTheAccountExistsOrNot()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var userId = await SeedAccountAsync(host, phoneConfirmed: true);

        var existing = await ForgotBySmsAsync(host);
        var unknown = await ForgotBySmsAsync(host, "nadie@demo.com");

        // Each request only leaves its event in the outbox: it neither reads nor changes the account, so it answers
        // in the same time, and the account is found after the answer.
        Assert.True(existing.IsSuccess);
        Assert.True(unknown.IsSuccess);
        Assert.Equal(
            [(VerificationCodePurpose.PasswordReset, Email), (VerificationCodePurpose.PasswordReset, "nadie@demo.com")],
            host.Outbox.Added.Select(added => (((VerificationCodeRequestedEvent)added.Event).Purpose, ((VerificationCodeRequestedEvent)added.Event).Email)));
        Assert.All(host.Outbox.Added, added => Assert.Null(added.TenantId));
        Assert.Null(await host.QueryAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.SmsCodeSentAt).SingleAsync()));
        Assert.Empty(host.Sms.Codes);
    }

    [Fact]
    public async Task LateCodeRequest_ShouldSendNothing()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedAccountAsync(host, phoneConfirmed: true);
        var requested = new VerificationCodeRequestedEvent(VerificationCodePurpose.PasswordReset, Email: Email);

        await host.InScopeAsync(async services =>
        {
            var now = services.GetRequiredService<IDateTimeProvider>().UtcNow;
            await ActivatorUtilities.CreateInstance<VerificationCodeRequestedHandler>(services).HandleAsync(
                requested,
                new OutboxEventContext(requested.EventId, null, null, null, now - VerificationCodeRequestedHandler.MaxDelay - TimeSpan.FromSeconds(1), 3),
                CancellationToken.None);
        });

        // The person has asked again or given up: a late code would only confuse them.
        Assert.Empty(host.Sms.Codes);
    }

    [Fact]
    public async Task ProviderFailure_ShouldGiveTheMinuteBack_SoTheRetrySendsTheCode()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var userId = await SeedAccountAsync(host, phoneConfirmed: true);
        var requested = new VerificationCodeRequestedEvent(VerificationCodePurpose.PasswordReset, Email: Email);

        await Assert.ThrowsAsync<HttpRequestException>(() => RunHandlerAsync(host, requested, new FailingSmsService()));
        var minuteAfterFailure = await host.QueryAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.SmsCodeSentAt).SingleAsync());
        await RunHandlerAsync(host, requested, host.Sms);

        // The outbox retries a handler that throws: with the minute given back, the retry sends a code.
        Assert.Null(minuteAfterFailure);
        var (phone, code) = Assert.Single(host.Sms.Codes);
        Assert.Equal(Phone, phone);
        Assert.True((await ResetBySmsAsync(host, code, "NuevaClave2")).IsSuccess);
    }

    [Fact]
    public async Task SmsCodes_ShouldBeOnePerMinute()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedAccountAsync(host, phoneConfirmed: true);

        await ForgotBySmsAsync(host);
        await ForgotBySmsAsync(host);
        await SendRequestedCodesAsync(host);

        Assert.Single(host.Sms.Codes);
    }

    [Fact]
    public async Task WrongCodes_ShouldLockTheAccount_UntilItIsRecovered()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var userId = await SeedAccountAsync(host, phoneConfirmed: true);
        await ForgotBySmsAsync(host);
        await SendRequestedCodesAsync(host);
        var code = host.Sms.Codes.Single().Code;
        var wrong = code == "000000" ? "111111" : "000000";

        var attempts = new List<Result>();
        for (var i = 0; i < 5; i++)
            attempts.Add(await ResetBySmsAsync(host, wrong, "NuevaClave2"));
        var rightCodeTooLate = await ResetBySmsAsync(host, code, "NuevaClave2");

        // Anonymous: the lock is not told apart from a wrong code, or it would say the account exists.
        Assert.All(attempts, attempt => Assert.Equal(AccountSecurity.SmsRecoveryFailedMessage, attempt.Error));
        Assert.Equal(AccountSecurity.SmsRecoveryFailedMessage, rightCodeTooLate.Error);
        Assert.True(await IsLockedOutAsync(host, userId));
        Assert.True(await CheckPasswordAsync(host, userId, Password));
    }

    [Fact]
    public async Task SmsReset_ShouldAnswerTheSame_WhetherTheAccountExistsOrNot()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedAccountAsync(host, phoneConfirmed: true);
        await ForgotBySmsAsync(host);
        await SendRequestedCodesAsync(host);
        var wrong = host.Sms.Codes.Single().Code == "000000" ? "111111" : "000000";

        var unknownAccount = await ResetBySmsAsync(host, wrong, "NuevaClave2", "nadie@demo.com");
        var wrongCode = await ResetBySmsAsync(host, wrong, "NuevaClave2");

        Assert.Equal(AccountSecurity.SmsRecoveryFailedMessage, unknownAccount.Error);
        Assert.Equal(unknownAccount.Error, wrongCode.Error);
    }

    [Fact]
    public async Task SmsReset_ShouldAnswerTheSame_ForAPhoneThePersonNeverConfirmed()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        await SeedAccountAsync(host, phoneConfirmed: false);

        var result = await ResetBySmsAsync(host, "123456", "NuevaClave2");

        Assert.Equal(AccountSecurity.SmsRecoveryFailedMessage, result.Error);
    }

    [Fact]
    public async Task PhoneChange_ShouldNeedThePassword_AndTheCodeSentToTheNewNumber()
    {
        await using var host = await SqliteTestHost.CreateAsync("CUSTOMER");
        var userId = await SeedAccountAsync(host, phoneConfirmed: true, role: UserRole.Customer);
        var me = new SignedIn(userId);

        var wrongPassword = await RequestPhoneChangeAsync(host, me, "55 8765 4321", "Incorrecta1");
        var requested = await RequestPhoneChangeAsync(host, me, "55 8765 4321", Password);
        var (sentTo, code) = Assert.Single(host.Sms.Codes);
        var wrongCode = await ConfirmPhoneChangeAsync(host, me, "55 8765 4321", code == "000000" ? "111111" : "000000");
        var confirmed = await ConfirmPhoneChangeAsync(host, me, "55 8765 4321", code);

        Assert.Equal(AccountSecurity.WrongPasswordMessage, wrongPassword.Error);
        Assert.True(requested.IsSuccess, requested.Error);
        Assert.Equal("+525587654321", sentTo);
        Assert.Equal(AccountSecurity.InvalidCodeMessage, wrongCode.Error);
        Assert.True(confirmed.IsSuccess, confirmed.Error);
        var user = await UserAsync(host, userId);
        Assert.Equal("+525587654321", user.PhoneNumber);
        Assert.True(user.PhoneNumberConfirmed);
        // A customer's profile follows the number it confirmed.
        Assert.Equal("+525587654321", await host.QueryAsync(db => db.CustomerProfiles.Where(p => p.UserId == userId).Select(p => p.PhoneNormalized).SingleAsync()));
    }

    [Fact]
    public async Task UnconfirmedNumber_ShouldBeFixedWithThePassword_OnlyBeforeTheAccountConfirmsAnything()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var userId = await SeedAccountAsync(host, phoneConfirmed: false);

        var wrongPassword = await ChangeUnconfirmedPhoneAsync(host, userId, "Incorrecta1");
        var changed = await ChangeUnconfirmedPhoneAsync(host, userId, Password);
        await UpdateUserAsync(host, userId, user => user.EmailConfirmed = true);
        var afterConfirmingTheEmail = await ChangeUnconfirmedPhoneAsync(host, userId, Password);

        Assert.Equal(AccountSecurity.WrongPasswordMessage, wrongPassword.Error);
        Assert.True(changed.IsSuccess, changed.Error);
        Assert.Equal("+525587654321", (await UserAsync(host, userId)).PhoneNumber);
        Assert.Equal(["+525587654321"], host.Sms.VerificationRecipients);
        Assert.Equal(ConfirmPhoneCommandHandler.ConfirmFromProfileMessage, afterConfirmingTheEmail.Error);
    }

    [Fact]
    public async Task AnonymousConfirmation_ShouldNotConfirmAPhone_OnceTheAccountSignsIn()
    {
        await using var host = await SqliteTestHost.CreateAsync();
        var userId = await SeedAccountAsync(host, phoneConfirmed: false, emailConfirmed: true);
        var code = await host.InScopeAsync(async services =>
        {
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId.ToString());
            return await userManager.GenerateChangePhoneNumberTokenAsync(user!, Phone);
        });

        var result = await host.HandleAsync<ConfirmPhoneCommandHandler, Result>(handler => handler.Handle(
            new ConfirmPhoneCommand { UserId = userId.ToString(), Code = code }, CancellationToken.None));

        // Whoever typed the number (an admin, a stolen form) must not confirm it and recover the password by SMS.
        Assert.Equal(ConfirmPhoneCommandHandler.ConfirmFromProfileMessage, result.Error);
        Assert.False((await UserAsync(host, userId)).PhoneNumberConfirmed);
    }

    private static Task<Guid> SeedAccountAsync(SqliteTestHost host, bool phoneConfirmed, bool emailConfirmed = false, UserRole role = UserRole.Staff)
        => host.InScopeAsync(async services =>
        {
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = Email,
                Email = Email,
                FirstName = "Ana",
                LastName = "Demo",
                PhoneNumber = Phone,
                PhoneNumberConfirmed = phoneConfirmed,
                EmailConfirmed = emailConfirmed,
                Role = role,
                IsActive = true
            };
            var created = await userManager.CreateAsync(user, Password);
            Assert.True(created.Succeeded, created.ToErrorMessage());
            if (role == UserRole.Customer)
            {
                var db = services.GetRequiredService<ApplicationDbContext>();
                db.CustomerProfiles.Add(new Domain.Customers.Entities.CustomerProfile(user.Id, Phone, "Ana Demo", "Ana", "Demo"));
                await db.SaveChangesAsync();
            }

            return user.Id;
        });

    private static Task<Result> ForgotBySmsAsync(SqliteTestHost host, string email = Email)
        => host.HandleAsync<ForgotPasswordBySmsCommandHandler, Result>(handler =>
            handler.Handle(new ForgotPasswordBySmsCommand { Email = email }, CancellationToken.None));

    private static Task SendRequestedCodesAsync(SqliteTestHost host)
        => host.DispatchAsync<VerificationCodeRequestedEvent, VerificationCodeRequestedHandler>();

    private static Task RunHandlerAsync(SqliteTestHost host, VerificationCodeRequestedEvent requested, ISmsService sms)
        => host.InScopeAsync(services => ActivatorUtilities.CreateInstance<VerificationCodeRequestedHandler>(services, sms).HandleAsync(
            requested,
            new OutboxEventContext(requested.EventId, null, null, null, services.GetRequiredService<IDateTimeProvider>().UtcNow, 1),
            CancellationToken.None));

    private static Task<Result> ResetBySmsAsync(SqliteTestHost host, string code, string newPassword, string email = Email)
        => host.HandleAsync<ResetPasswordWithSmsCodeCommandHandler, Result>(handler =>
            handler.Handle(new ResetPasswordWithSmsCodeCommand { Email = email, Code = code, NewPassword = newPassword }, CancellationToken.None));

    private static Task<Result> RequestPhoneChangeAsync(SqliteTestHost host, ICurrentUserService me, string phone, string password)
        => host.InScopeAsync(services => ActivatorUtilities
            .CreateInstance<RequestPhoneChangeCommandHandler>(services, me)
            .Handle(new RequestPhoneChangeCommand { PhoneNumber = phone, CurrentPassword = password }, CancellationToken.None));

    private static Task<Result> ConfirmPhoneChangeAsync(SqliteTestHost host, ICurrentUserService me, string phone, string code)
        => host.InScopeAsync(services => ActivatorUtilities
            .CreateInstance<ConfirmPhoneChangeCommandHandler>(services, me)
            .Handle(new ConfirmPhoneChangeCommand { PhoneNumber = phone, Code = code }, CancellationToken.None));

    private static Task<Result> ChangeUnconfirmedPhoneAsync(SqliteTestHost host, Guid userId, string password)
        => host.HandleAsync<ChangeUnconfirmedPhoneCommandHandler, Result>(handler => handler.Handle(
            new ChangeUnconfirmedPhoneCommand { UserId = userId, Password = password, PhoneNumber = "55 8765 4321" },
            CancellationToken.None));

    private static Task<ApplicationUser> UserAsync(SqliteTestHost host, Guid userId)
        => host.QueryAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == userId));

    private static Task UpdateUserAsync(SqliteTestHost host, Guid userId, Action<ApplicationUser> change)
        => host.InScopeAsync(async services =>
        {
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId.ToString());
            change(user!);
            Assert.True((await userManager.UpdateAsync(user!)).Succeeded);
        });

    private static Task<bool> CheckPasswordAsync(SqliteTestHost host, Guid userId, string password)
        => host.InScopeAsync(async services =>
        {
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId.ToString());
            return await userManager.CheckPasswordAsync(user!, password);
        });

    private static Task<bool> IsLockedOutAsync(SqliteTestHost host, Guid userId)
        => host.InScopeAsync(async services =>
        {
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByIdAsync(userId.ToString());
            return await userManager.IsLockedOutAsync(user!);
        });

    private sealed class FailingSmsService : ISmsService
    {
        public Task SendVerificationCodeAsync(string phoneNumber, string code)
            => throw new HttpRequestException("The provider did not answer.");

        public Task SendSmsAsync(string phoneNumber, string message) => Task.CompletedTask;
    }

    private sealed class SignedIn(Guid userId) : ICurrentUserService
    {
        public string? UserId { get; } = userId.ToString();
        public string? UserName => Email;
        public string? Role => "STAFF";
        public string? TenantRole => null;
        public IReadOnlyCollection<string> Permissions => [];
        public Guid? TenantId => null;
    }
}
