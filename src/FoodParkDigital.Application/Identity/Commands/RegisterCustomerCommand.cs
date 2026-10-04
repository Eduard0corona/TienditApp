using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

[AllowAnonymousRequest("Acceso público: inicio de sesión, registro y confirmaciones.")]
public record RegisterCustomerCommand : IRequest<Result<AuthResponse>>
{
    public string Email { get; init; } = null!;
    public string Password { get; init; } = null!;
    public string FirstName { get; init; } = null!;
    public string LastName { get; init; } = null!;
    public string PhoneNumber { get; init; } = null!;
}

public class RegisterCustomerCommandValidator : AbstractValidator<RegisterCustomerCommand>
{
    public RegisterCustomerCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PhoneNumber).NotEmpty();
    }
}

public class RegisterCustomerCommandHandler : IRequestHandler<RegisterCustomerCommand, Result<AuthResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IApplicationDbContext _context;
    private readonly IPhoneNormalizationService _phoneNormalizationService;
    private readonly IEmailService _emailService;
    private readonly ISmsService _smsService;

    public RegisterCustomerCommandHandler(
        UserManager<ApplicationUser> userManager,
        IJwtTokenService jwtTokenService,
        IApplicationDbContext context,
        IPhoneNormalizationService phoneNormalizationService,
        IEmailService emailService,
        ISmsService smsService)
    {
        _userManager = userManager;
        _jwtTokenService = jwtTokenService;
        _context = context;
        _phoneNormalizationService = phoneNormalizationService;
        _emailService = emailService;
        _smsService = smsService;
    }

    public async Task<Result<AuthResponse>> Handle(RegisterCustomerCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.ToUpperInvariant();
        var existingUser = await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        if (existingUser)
            return Result.Failure<AuthResponse>("Ya existe una cuenta con este correo electronico.");

        if (!_phoneNormalizationService.TryNormalizeToE164(
                request.PhoneNumber,
                CustomerPhoneOwnershipRules.DefaultCountry,
                out var phoneNormalized))
        {
            return Result.Failure<AuthResponse>(PhoneNumberRules.InvalidPhoneMessage);
        }

        var phoneHolder = await CustomerPhoneOwnershipRules.FindHolderAsync(
            _context,
            _phoneNormalizationService,
            phoneNormalized,
            cancellationToken);

        // Only a number its account confirmed by SMS is taken: an unverified profile must not let
        // whoever typed the number first lock its real owner out.
        if (phoneHolder is { IsVerified: true })
            return Result.Failure<AuthResponse>("Ya existe una cuenta de cliente con este telefono.");

        var user = new ApplicationUser
        {
            DefaultTenantId = null,
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            Role = UserRole.Customer,
            EmailConfirmed = false,
            IsActive = true
        };

        // Identity checks the password only inside CreateAsync; reject it before writing anything
        var passwordResult = await _userManager.ValidatePasswordPolicyAsync(user, request.Password);
        if (!passwordResult.Succeeded)
            return Result.Failure<AuthResponse>(passwordResult.ToErrorMessage());

        // User, role, profile and session are committed together or not at all
        var registration = await _context.ExecuteInResilientTransactionAsync(async ct =>
        {
            var createResult = await _userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
                return Result.Failure<RefreshToken>(createResult.ToErrorMessage());

            var roleResult = await _userManager.AddToRoleAsync(user, "CUSTOMER");
            if (!roleResult.Succeeded)
                return Result.Failure<RefreshToken>(roleResult.ToErrorMessage());

            // While an unverified profile holds the number this account starts without one;
            // ConfirmPhoneCommand gives the number to whichever account proves it by SMS.
            if (phoneHolder is null)
            {
                _context.CustomerProfiles.Add(new CustomerProfile(
                    user.Id,
                    phoneNormalized,
                    $"{request.FirstName} {request.LastName}".Trim(),
                    request.FirstName,
                    request.LastName));
            }

            var sessionToken = _jwtTokenService.GenerateRefreshToken(user.Id);
            _context.RefreshTokens.Add(sessionToken);
            await _context.SaveChangesAsync(ct);

            return Result.Success(sessionToken);
        }, cancellationToken);

        if (registration.IsFailure)
            return Result.Failure<AuthResponse>(registration.Error!);

        // Send confirmations only after the registration is committed. Guest orders placed with this
        // phone are linked by ConfirmPhoneCommand once the phone is verified, not here.
        var emailToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        await _emailService.SendEmailConfirmationAsync(user.Email!, user.Id.ToString(), emailToken);

        var phoneCode = await _userManager.GenerateChangePhoneNumberTokenAsync(user, request.PhoneNumber);
        await _smsService.SendVerificationCodeAsync(request.PhoneNumber, phoneCode);

        var accessToken = _jwtTokenService.GenerateAccessToken(user);
        var refreshToken = registration.Value;

        return Result.Success(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(60),
            RequiresTwoFactor = false,
            UserId = user.Id.ToString(),
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email!,
                FirstName = user.FirstName,
                LastName = user.LastName,
                PhoneNumber = user.PhoneNumber,
                PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                Role = user.Role.ToString(),
                TenantId = user.DefaultTenantId,
                IsActive = user.IsActive,
                AvatarUrl = null,
                AvatarImages = null
            }
        });
    }
}
