using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

/// <summary>
/// The phone of an account recovers its password by SMS, so it only changes with the password and a code sent to the
/// new number: a stolen session or an admin cannot move the account to their phone.
/// </summary>
[RequiresAuthentication]
public record RequestPhoneChangeCommand : IRequest<Result>
{
    public string PhoneNumber { get; init; } = null!;
    public string CurrentPassword { get; init; } = null!;
}

public class RequestPhoneChangeCommandValidator : AbstractValidator<RequestPhoneChangeCommand>
{
    public RequestPhoneChangeCommandValidator()
    {
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(30);
        RuleFor(x => x.CurrentPassword).NotEmpty();
    }
}

public class RequestPhoneChangeCommandHandler : IRequestHandler<RequestPhoneChangeCommand, Result>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPhoneNormalizationService _phones;
    private readonly ISmsService _smsService;
    private readonly IDateTimeProvider _clock;

    public RequestPhoneChangeCommandHandler(
        UserManager<ApplicationUser> userManager,
        ICurrentUserService currentUserService,
        IPhoneNormalizationService phones,
        ISmsService smsService,
        IDateTimeProvider clock)
    {
        _userManager = userManager;
        _currentUserService = currentUserService;
        _phones = phones;
        _smsService = smsService;
        _clock = clock;
    }

    public async Task<Result> Handle(RequestPhoneChangeCommand request, CancellationToken cancellationToken)
    {
        var user = await PhoneChange.CurrentUserAsync(_userManager, _currentUserService);
        if (user is null)
            return Result.Failure("Usuario no encontrado.");

        var password = await AccountSecurity.VerifyPasswordAsync(_userManager, user, request.CurrentPassword);
        if (password != CredentialCheck.Valid)
            return Result.Failure(AccountSecurity.PasswordMessageFor(password)!);

        if (!CustomerPhones.TryNormalize(_phones, request.PhoneNumber, out var phone))
            return Result.Failure(CustomerPhones.InvalidMessage);
        if (user.PhoneNumberConfirmed && PhoneChange.IsSame(_phones, user.PhoneNumber, phone))
            return Result.Failure("Ese ya es tu número.");
        if (!await AccountSecurity.TryReserveSmsCodeAsync(_userManager, user, _clock.UtcNow))
            return Result.Failure(AccountSecurity.SmsCodeWaitMessage);

        var code = await _userManager.GenerateChangePhoneNumberTokenAsync(user, phone);
        await _smsService.SendVerificationCodeAsync(phone, code);
        return Result.Success();
    }
}

[RequiresAuthentication]
public record ConfirmPhoneChangeCommand : IRequest<Result>
{
    public string PhoneNumber { get; init; } = null!;
    public string Code { get; init; } = null!;
}

public class ConfirmPhoneChangeCommandValidator : AbstractValidator<ConfirmPhoneChangeCommand>
{
    public ConfirmPhoneChangeCommandValidator()
    {
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Code).NotEmpty().Matches(@"^\d{5,6}$").WithMessage("El código tiene 5 o 6 dígitos.");
    }
}

public class ConfirmPhoneChangeCommandHandler : IRequestHandler<ConfirmPhoneChangeCommand, Result>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUserService;
    private readonly IApplicationDbContext _context;
    private readonly IPhoneNormalizationService _phones;
    private readonly ITemporaryCustomerClaimService _claimService;

    public ConfirmPhoneChangeCommandHandler(
        UserManager<ApplicationUser> userManager,
        ICurrentUserService currentUserService,
        IApplicationDbContext context,
        IPhoneNormalizationService phones,
        ITemporaryCustomerClaimService claimService)
    {
        _userManager = userManager;
        _currentUserService = currentUserService;
        _context = context;
        _phones = phones;
        _claimService = claimService;
    }

    public async Task<Result> Handle(ConfirmPhoneChangeCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var userId))
            return Result.Failure("Usuario no encontrado.");
        if (!CustomerPhones.TryNormalize(_phones, request.PhoneNumber, out var phone))
            return Result.Failure(CustomerPhones.InvalidMessage);

        // The code is checked (and a wrong one counted) before the change, which commits with the customer profile.
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
            return Result.Failure("Usuario no encontrado.");
        var code = AccountSecurity.NormalizeCode(request.Code);
        var check = await AccountSecurity.VerifyCodeAsync(
            _userManager, user, _userManager.Options.Tokens.ChangePhoneNumberTokenProvider, PhoneChange.TokenPurpose(phone), code ?? string.Empty);
        if (check != CredentialCheck.Valid)
            return Result.Failure(AccountSecurity.CodeMessageFor(check)!);

        return await _context.ExecuteInResilientTransactionAsync(async ct =>
        {
            var account = await _userManager.FindByIdAsync(userId.ToString());
            if (account is null)
                return Result.Failure("Usuario no encontrado.");

            var changed = await _userManager.ChangePhoneNumberAsync(account, phone, code!);
            if (!changed.Succeeded)
                return Result.Failure(AccountSecurity.InvalidCodeMessage);

            if (account.Role == UserRole.Customer)
            {
                // Like confirming the phone at registration: orders placed with this number are theirs from now on.
                await _claimService.ClaimByPhoneAsync(phone, account.Id, CustomerPhoneOwnershipRules.DefaultCountry, ct);
                await CustomerPhoneOwnershipRules.AssignConfirmedPhoneAsync(_context, _phones, account, phone, ct);
            }

            return Result.Success();
        }, cancellationToken);
    }
}

/// <summary>
/// The number typed at registration was wrong: before the account can sign in, the person fixes it with their
/// password and receives the code there.
/// </summary>
[AllowAnonymousRequest("Acceso público: corregir el teléfono de una cuenta que se está registrando.")]
public record ChangeUnconfirmedPhoneCommand : IRequest<Result>
{
    public Guid UserId { get; init; }
    public string Password { get; init; } = null!;
    public string PhoneNumber { get; init; } = null!;
}

public class ChangeUnconfirmedPhoneCommandValidator : AbstractValidator<ChangeUnconfirmedPhoneCommand>
{
    public ChangeUnconfirmedPhoneCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(30);
    }
}

public class ChangeUnconfirmedPhoneCommandHandler : IRequestHandler<ChangeUnconfirmedPhoneCommand, Result>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _context;
    private readonly IPhoneNormalizationService _phones;
    private readonly ISmsService _smsService;
    private readonly IDateTimeProvider _clock;

    public ChangeUnconfirmedPhoneCommandHandler(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context,
        IPhoneNormalizationService phones,
        ISmsService smsService,
        IDateTimeProvider clock)
    {
        _userManager = userManager;
        _context = context;
        _phones = phones;
        _smsService = smsService;
        _clock = clock;
    }

    public async Task<Result> Handle(ChangeUnconfirmedPhoneCommand request, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters: an account being registered has no business in context; filtered by its id.
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        // Only before the account confirms anything: from then on the number changes from the profile.
        if (user is null || !user.IsActive || user.EmailConfirmed || user.PhoneNumberConfirmed || user.PasswordHash is null)
            return Result.Failure(ConfirmPhoneCommandHandler.ConfirmFromProfileMessage);

        var password = await AccountSecurity.VerifyPasswordAsync(_userManager, user, request.Password);
        if (password != CredentialCheck.Valid)
            return Result.Failure(AccountSecurity.PasswordMessageFor(password)!);

        if (!CustomerPhones.TryNormalize(_phones, request.PhoneNumber, out var phone))
            return Result.Failure(CustomerPhones.InvalidMessage);
        if (!await AccountSecurity.TryReserveSmsCodeAsync(_userManager, user, _clock.UtcNow))
            return Result.Failure(AccountSecurity.SmsCodeWaitMessage);

        user.PhoneNumber = phone;
        var updated = await _userManager.UpdateAsync(user);
        if (!updated.Succeeded)
            return Result.Failure(updated.ToErrorMessage());

        var code = await _userManager.GenerateChangePhoneNumberTokenAsync(user, phone);
        await _smsService.SendVerificationCodeAsync(phone, code);
        return Result.Success();
    }
}

internal static class PhoneChange
{
    /// <summary>The purpose Identity gives the codes of <see cref="UserManager{TUser}.GenerateChangePhoneNumberTokenAsync"/>.</summary>
    public static string TokenPurpose(string phone) => UserManager<ApplicationUser>.ChangePhoneNumberTokenPurpose + ":" + phone;

    public static async Task<ApplicationUser?> CurrentUserAsync(UserManager<ApplicationUser> userManager, ICurrentUserService currentUserService)
    {
        if (string.IsNullOrEmpty(currentUserService.UserId))
            return null;
        var user = await userManager.FindByIdAsync(currentUserService.UserId);
        return user is { IsActive: true } ? user : null;
    }

    public static bool IsSame(IPhoneNormalizationService phones, string? stored, string normalized)
        => CustomerPhones.TryNormalize(phones, stored, out var current) && current == normalized;
}
