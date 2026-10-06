using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Identity.Services;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Identity.Events;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

/// <summary>
/// Recovering the password with a code sent by SMS to the phone the account confirmed (for people who do not check
/// their email). The account is the email they sign in with; a phone typed by someone else and never confirmed by
/// the person gets nothing.
/// </summary>
public static class SmsPasswordReset
{
    public const string TokenPurpose = "ResetPasswordBySms";
}

[AllowAnonymousRequest("Acceso público: recuperar la contraseña con un código por SMS.")]
public record ForgotPasswordBySmsCommand : IRequest<Result>
{
    public string Email { get; init; } = null!;
}

public class ForgotPasswordBySmsCommandValidator : AbstractValidator<ForgotPasswordBySmsCommand>
{
    public ForgotPasswordBySmsCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
    }
}

public class ForgotPasswordBySmsCommandHandler : IRequestHandler<ForgotPasswordBySmsCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly IOutbox _outbox;

    public ForgotPasswordBySmsCommandHandler(IApplicationDbContext context, IOutbox outbox)
    {
        _context = context;
        _outbox = outbox;
    }

    public async Task<Result> Handle(ForgotPasswordBySmsCommand request, CancellationToken cancellationToken)
    {
        // The same work and answer for every email: the account is found, and the code sent, after the answer
        // (VerificationCodeRequestedHandler).
        _outbox.Add(new VerificationCodeRequestedEvent(VerificationCodePurpose.PasswordReset, Email: request.Email.Trim()), tenantId: null);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

[AllowAnonymousRequest("Acceso público: recuperar la contraseña con un código por SMS.")]
public record ResetPasswordWithSmsCodeCommand : IRequest<Result>
{
    public string Email { get; init; } = null!;
    public string Code { get; init; } = null!;
    public string NewPassword { get; init; } = null!;
}

public class ResetPasswordWithSmsCodeCommandValidator : AbstractValidator<ResetPasswordWithSmsCodeCommand>
{
    public ResetPasswordWithSmsCodeCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Code).NotEmpty().Matches(@"^\d{5,6}$").WithMessage("El código tiene 5 o 6 dígitos.");
        RuleFor(x => x.NewPassword).NewPassword();
    }
}

public class ResetPasswordWithSmsCodeCommandHandler : IRequestHandler<ResetPasswordWithSmsCodeCommand, Result>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public ResetPasswordWithSmsCodeCommandHandler(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context,
        IDateTimeProvider clock)
    {
        _userManager = userManager;
        _context = context;
        _clock = clock;
    }

    public async Task<Result> Handle(ResetPasswordWithSmsCodeCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        // IgnoreQueryFilters: there is no business in context before signing in; filtered by the account's email.
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
        // One answer for every failure, a locked account included: telling it apart would say the account exists.
        if (user is null || !user.IsActive || !user.PhoneNumberConfirmed)
            return Result.Failure(AccountSecurity.SmsRecoveryFailedMessage);

        var check = await AccountSecurity.VerifyCodeAsync(
            _userManager, user, TokenOptions.DefaultPhoneProvider, SmsPasswordReset.TokenPurpose, request.Code);
        if (check != CredentialCheck.Valid)
            return Result.Failure(AccountSecurity.SmsRecoveryFailedMessage);

        // The code proved the phone; the password is changed through a reset token of the same request.
        var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var reset = await _userManager.ResetPasswordAsync(user, resetToken, request.NewPassword);
        if (!reset.Succeeded)
            return Result.Failure(reset.ToErrorMessage());

        await AccountSecurity.UnlockAsync(_userManager, user);
        // Like the reset by email: the sessions open with the old password end (the reset also changed the stamp
        // their access tokens carry).
        await UserSessions.EndAllAsync(
            _context, user.Id, RefreshTokenRevocationReason.PasswordReset, _clock.UtcNow, cancellationToken);
        return Result.Success();
    }
}
