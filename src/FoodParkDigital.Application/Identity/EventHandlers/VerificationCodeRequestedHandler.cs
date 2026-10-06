using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Identity.Commands;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Events;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Application.Identity.EventHandlers;

/// <summary>
/// Sends the code a request without a session asked for, after its answer: finds the account, checks it may receive
/// one (one per minute), generates it and sends it. An account that does not exist or may not receive it gets nothing,
/// silently, as the request already answered the same for everyone.
/// </summary>
public sealed class VerificationCodeRequestedHandler : IOutboxEventHandler<VerificationCodeRequestedEvent>
{
    /// <summary>Past this, the person has asked again or given up: a late code would only confuse them.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(10);

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _context;
    private readonly ISmsService _sms;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<VerificationCodeRequestedHandler> _logger;

    public VerificationCodeRequestedHandler(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context,
        ISmsService sms,
        IDateTimeProvider clock,
        ILogger<VerificationCodeRequestedHandler> logger)
    {
        _userManager = userManager;
        _context = context;
        _sms = sms;
        _clock = clock;
        _logger = logger;
    }

    public async Task HandleAsync(
        VerificationCodeRequestedEvent domainEvent,
        OutboxEventContext context,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        if (now - context.OccurredAt > MaxDelay)
        {
            _logger.LogWarning("A verification code asked for at {RequestedAt} was not sent: too late.", context.OccurredAt);
            return;
        }

        var user = await FindAccountAsync(domainEvent, cancellationToken);
        if (user is null)
            return;

        var lastCodeSentAt = user.SmsCodeSentAt;
        if (!await AccountSecurity.TryReserveSmsCodeAsync(_userManager, user, now))
            return;

        var code = domainEvent.Purpose == VerificationCodePurpose.PasswordReset
            ? await _userManager.GenerateUserTokenAsync(user, TokenOptions.DefaultPhoneProvider, SmsPasswordReset.TokenPurpose)
            : await _userManager.GenerateChangePhoneNumberTokenAsync(user, user.PhoneNumber!);
        try
        {
            await _sms.SendVerificationCodeAsync(user.PhoneNumber!, code);
        }
        catch
        {
            // The provider did not take it: the minute goes back, so the retry of the outbox sends another code.
            await GiveTheMinuteBackAsync(user, lastCodeSentAt);
            throw;
        }
    }

    /// <returns>The account, when it may receive this code; otherwise null.</returns>
    private async Task<ApplicationUser?> FindAccountAsync(VerificationCodeRequestedEvent request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: accounts belong to no business, and the request came without a session.
        var users = _context.Users.IgnoreQueryFilters();
        if (request.Purpose == VerificationCodePurpose.PasswordReset)
        {
            var normalizedEmail = request.Email?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(normalizedEmail))
                return null;
            var account = await users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
            // Only through a phone the person confirmed: one typed by someone else gets nothing.
            return account is { IsActive: true, PhoneNumberConfirmed: true } && !string.IsNullOrWhiteSpace(account.PhoneNumber)
                ? account
                : null;
        }

        // The confirmation screen identifies the account by the id it received at sign-up (the phone is stored as
        // typed, so it may not match an E.164 search).
        var unconfirmed = request.UserId is { } userId
            ? await users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            : await users.FirstOrDefaultAsync(u => u.PhoneNumber == request.PhoneNumber, cancellationToken);
        return unconfirmed is { PhoneNumberConfirmed: false } && !string.IsNullOrWhiteSpace(unconfirmed.PhoneNumber)
            ? unconfirmed
            : null;
    }

    private async Task GiveTheMinuteBackAsync(ApplicationUser user, DateTimeOffset? lastCodeSentAt)
    {
        try
        {
            user.SmsCodeSentAt = lastCodeSentAt;
            await _userManager.UpdateAsync(user);
        }
        catch (Exception ex)
        {
            // Without it the retry finds the minute taken and sends nothing; the person asks again after it.
            _logger.LogWarning("The minute of a verification code that was not sent could not be given back ({ErrorType}).", ex.GetType().Name);
        }
    }
}
