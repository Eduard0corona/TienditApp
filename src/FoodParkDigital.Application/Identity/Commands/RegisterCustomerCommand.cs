using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

/// <summary>
/// Signs up a customer account and sends the link that confirms its email. It opens no session: the account is
/// activated from that link and the customer then signs in with their credentials. Returns the id of the new account.
/// </summary>
[AllowAnonymousRequest("Acceso público: inicio de sesión, registro y confirmaciones.")]
public record RegisterCustomerCommand : IRequest<Result<Guid>>
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
        RuleFor(x => x.Password).NewPassword();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PhoneNumber).NotEmpty();
    }
}

public class RegisterCustomerCommandHandler : IRequestHandler<RegisterCustomerCommand, Result<Guid>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _context;
    private readonly IPhoneNormalizationService _phoneNormalizationService;
    private readonly IEmailService _emailService;

    public RegisterCustomerCommandHandler(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context,
        IPhoneNormalizationService phoneNormalizationService,
        IEmailService emailService)
    {
        _userManager = userManager;
        _context = context;
        _phoneNormalizationService = phoneNormalizationService;
        _emailService = emailService;
    }

    public async Task<Result<Guid>> Handle(RegisterCustomerCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.ToUpperInvariant();
        var existingUser = await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        if (existingUser)
            return Result.Failure<Guid>("Ya existe una cuenta con este correo electrónico.");

        if (!_phoneNormalizationService.TryNormalizeToE164(
                request.PhoneNumber,
                CustomerPhoneOwnershipRules.DefaultCountry,
                out var phoneNormalized))
        {
            return Result.Failure<Guid>(PhoneNumberRules.InvalidPhoneMessage);
        }

        var phoneHolder = await CustomerPhoneOwnershipRules.FindHolderAsync(
            _context,
            _phoneNormalizationService,
            phoneNormalized,
            cancellationToken);

        // Only a number its account confirmed by SMS is taken: an unverified profile must not let
        // whoever typed the number first lock its real owner out.
        if (phoneHolder is { IsVerified: true })
            return Result.Failure<Guid>("Ya existe una cuenta de cliente con este teléfono.");

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
            return Result.Failure<Guid>(passwordResult.ToErrorMessage());

        // User, role and profile are committed together or not at all
        var registration = await _context.ExecuteInResilientTransactionAsync(async ct =>
        {
            var createResult = await _userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
                return Result.Failure(createResult.ToErrorMessage());

            var roleResult = await _userManager.AddToRoleAsync(user, "CUSTOMER");
            if (!roleResult.Succeeded)
                return Result.Failure(roleResult.ToErrorMessage());

            // While an unverified profile holds the number this account starts without one; confirming the
            // phone by SMS (from the profile) gives the number to whichever account proves it.
            if (phoneHolder is null)
            {
                _context.CustomerProfiles.Add(new CustomerProfile(
                    user.Id,
                    phoneNormalized,
                    $"{request.FirstName} {request.LastName}".Trim(),
                    request.FirstName,
                    request.LastName));
            }

            await _context.SaveChangesAsync(ct);

            return Result.Success();
        }, cancellationToken);

        if (registration.IsFailure)
            return Result.Failure<Guid>(registration.Error!);

        // The link goes out only after the registration is committed. It activates the account (signing in needs a
        // confirmed email or phone). Guest orders placed with this phone are linked once the phone is verified, not here.
        var emailToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        await _emailService.SendEmailConfirmationAsync(user.Email!, user.Id.ToString(), emailToken);

        return Result.Success(user.Id);
    }
}
