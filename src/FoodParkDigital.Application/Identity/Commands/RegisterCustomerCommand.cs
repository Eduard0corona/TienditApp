using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Identity.Events;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

/// <summary>
/// Signs up a customer account and sends the link that confirms its email. It opens no session: the account is
/// activated from that link and the customer then signs in with their credentials.
/// <para>An email that already has an account answers the same and creates nothing: that account gets an email
/// instead (<see cref="AccountEmailPurpose.SignUpWithExistingEmail"/>), so the answer never tells whether an email has
/// an account. Returns the id of the account of the email, new or not: never send it back to whoever signed up.</para>
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
    private readonly IOutbox _outbox;

    public RegisterCustomerCommandHandler(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context,
        IPhoneNormalizationService phoneNormalizationService,
        IOutbox outbox)
    {
        _userManager = userManager;
        _context = context;
        _phoneNormalizationService = phoneNormalizationService;
        _outbox = outbox;
    }

    public async Task<Result<Guid>> Handle(RegisterCustomerCommand request, CancellationToken cancellationToken)
    {
        // What was typed is checked before looking for the email: its rules never depend on whether it has an account.
        if (!PhoneNumberRules.TryNormalizeSignUpPhone(_phoneNormalizationService, request.PhoneNumber, out var phoneNormalized))
            return Result.Failure<Guid>(PhoneNumberRules.InvalidPhoneMessage);

        var user = new ApplicationUser
        {
            DefaultTenantId = null,
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            // The number as its profile and its SMS codes use it, not as it was typed.
            PhoneNumber = phoneNormalized,
            Role = UserRole.Customer,
            EmailConfirmed = false,
            IsActive = true
        };

        // Identity checks the password only inside CreateAsync; reject it before writing anything
        var passwordResult = await _userManager.ValidatePasswordPolicyAsync(user, request.Password);
        if (!passwordResult.Succeeded)
            return Result.Failure<Guid>(passwordResult.ToErrorMessage());

        var phoneHolder = await CustomerPhoneOwnershipRules.FindHolderAsync(
            _context,
            _phoneNormalizationService,
            phoneNormalized,
            cancellationToken);

        // Only a number its account confirmed by SMS is taken: an unverified profile must not let
        // whoever typed the number first lock its real owner out. Checked before the email, for every email: after it,
        // a confirmed phone would answer this to a free email and success to one with an account.
        if (phoneHolder is { IsVerified: true })
            return Result.Failure<Guid>("Ya existe una cuenta de cliente con este teléfono.");

        var normalizedEmail = request.Email.ToUpperInvariant();
        // IgnoreQueryFilters: accounts belong to no business, and signing up comes without a session.
        var existingUserId = await _context.Users
            .IgnoreQueryFilters()
            .Where(u => u.NormalizedEmail == normalizedEmail)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingUserId is { } accountId)
        {
            SignUpTiming.HashAsIfCreating(_userManager, user, request.Password);
            _outbox.Add(new AccountEmailRequestedEvent(AccountEmailPurpose.SignUpWithExistingEmail, Email: request.Email), tenantId: null);
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success(accountId);
        }

        // User, role, profile and the request of the email that activates the account are committed together or not
        // at all
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

            // The link goes out from the outbox, after the commit, and is retried if the provider fails. It activates
            // the account (signing in needs a confirmed email or phone). Guest orders placed with this phone are linked
            // once the phone is verified, not here.
            _outbox.Add(new AccountEmailRequestedEvent(AccountEmailPurpose.EmailConfirmation, UserId: user.Id), tenantId: null);
            await _context.SaveChangesAsync(ct);

            return Result.Success();
        }, cancellationToken);

        if (registration.IsFailure)
            return Result.Failure<Guid>(registration.Error!);

        return Result.Success(user.Id);
    }
}
