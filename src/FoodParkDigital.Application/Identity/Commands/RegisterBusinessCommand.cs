using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Tenants;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Identity.Events;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

/// <summary>
/// Signs up a business with its owner and sends the link that confirms the owner's email. It opens no session:
/// the account is activated from that link and the owner then signs in with their credentials.
/// <para>An email that already has an account answers the same and creates nothing: that account gets an email
/// instead (<see cref="AccountEmailPurpose.SignUpWithExistingEmail"/>), so the answer never tells whether an email has
/// an account. Returns the id of the account of the email, new or not: never send it back to whoever signed up.</para>
/// </summary>
[AllowAnonymousRequest("Acceso público: inicio de sesión, registro y confirmaciones.")]
public record RegisterBusinessCommand : IRequest<Result<Guid>>
{
    // Personal info
    public string Email { get; init; } = null!;
    public string Password { get; init; } = null!;
    public string FirstName { get; init; } = null!;
    public string LastName { get; init; } = null!;
    public string PhoneNumber { get; init; } = null!;

    // Business info
    public string BusinessName { get; init; } = null!;
    public BusinessType BusinessType { get; init; }
    public string? Rfc { get; init; }
    public string? LegalName { get; init; }
    public string? Industry { get; init; }
    public string? TaxRegime { get; init; }
    public string? ContactPhone { get; init; }

    // Address
    public string? Street { get; init; }
    public string? ExteriorNumber { get; init; }
    public string? InteriorNumber { get; init; }
    public string? Neighborhood { get; init; }
    public string? ZipCode { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
}

public class RegisterBusinessCommandValidator : AbstractValidator<RegisterBusinessCommand>
{
    public RegisterBusinessCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NewPassword();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PhoneNumber).NotEmpty();

        RuleFor(x => x.BusinessName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BusinessType).IsInEnum();
        RuleFor(x => x.Rfc)
            .Matches(@"^[A-ZÑ&]{3,4}\d{6}[A-Z0-9]{3}$")
            .When(x => !string.IsNullOrWhiteSpace(x.Rfc))
            .WithMessage("El RFC no tiene un formato válido.");
        RuleFor(x => x.LegalName).MaximumLength(300);
        RuleFor(x => x.Industry).MaximumLength(200);
        RuleFor(x => x.TaxRegime).MaximumLength(200);
    }
}

public class RegisterBusinessCommandHandler : IRequestHandler<RegisterBusinessCommand, Result<Guid>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _context;
    private readonly IPhoneNormalizationService _phoneNormalizationService;
    private readonly IOutbox _outbox;
    private readonly IDateTimeProvider _clock;

    public RegisterBusinessCommandHandler(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context,
        IPhoneNormalizationService phoneNormalizationService,
        IOutbox outbox,
        IDateTimeProvider clock)
    {
        _userManager = userManager;
        _context = context;
        _phoneNormalizationService = phoneNormalizationService;
        _outbox = outbox;
        _clock = clock;
    }

    public async Task<Result<Guid>> Handle(RegisterBusinessCommand request, CancellationToken cancellationToken)
    {
        // The owner types the ten digits; the account keeps the number in E.164 because its SMS codes go to it. What
        // was typed is checked before looking for the email: its rules never depend on whether it has an account.
        if (!_phoneNormalizationService.TryNormalizeToE164(request.PhoneNumber, "MX", out var phoneNormalized))
            return Result.Failure<Guid>(PhoneNumberRules.InvalidPhoneMessage);

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = phoneNormalized,
            Role = UserRole.TenantAdmin,
            EmailConfirmed = false,
            IsActive = true
        };

        // Identity checks the password only inside CreateAsync; reject it before writing anything. Before looking for
        // the email, too: its rules depend on what was typed, never on whether the email has an account.
        var passwordResult = await _userManager.ValidatePasswordPolicyAsync(user, request.Password);
        if (!passwordResult.Succeeded)
            return Result.Failure<Guid>(passwordResult.ToErrorMessage());

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

        var slug = await BusinessProvisioning.UniqueSlugAsync(_context, request.BusinessName, cancellationToken);

        // Create tenant
        var tenant = new Tenant(
            name: request.BusinessName,
            slug: slug,
            businessType: request.BusinessType,
            contactEmail: request.Email);

        // Set business info
        Address? address = null;
        if (!string.IsNullOrWhiteSpace(request.Street) && !string.IsNullOrWhiteSpace(request.ZipCode))
        {
            address = new Address(
                street: request.Street,
                exteriorNumber: request.ExteriorNumber ?? "",
                interiorNumber: request.InteriorNumber,
                neighborhood: request.Neighborhood ?? "",
                zipCode: request.ZipCode,
                city: request.City ?? "",
                state: request.State ?? "",
                country: "México");
        }

        tenant.SetBusinessInfo(
            rfc: request.Rfc?.ToUpperInvariant(),
            legalName: request.LegalName,
            industry: request.Industry,
            taxRegime: request.TaxRegime,
            address: address);

        if (!string.IsNullOrWhiteSpace(request.ContactPhone))
            tenant.Update(tenant.Name, tenant.ContactEmail, request.ContactPhone, tenant.LogoUrl);

        // The owner, as TenantAdmin, starts in the new business
        user.DefaultTenantId = tenant.Id;

        // Tenant, owner, membership and the request of the email that activates the account are committed together
        // or not at all
        var registration = await _context.ExecuteInResilientTransactionAsync(async ct =>
        {
            await BusinessProvisioning.AddAsync(_context, tenant, _clock.UtcNow, ct);
            await _context.SaveChangesAsync(ct);

            var createResult = await _userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
                return Result.Failure(createResult.ToErrorMessage());

            var roleResult = await _userManager.AddToRoleAsync(user, "TENANT_ADMIN");
            if (!roleResult.Succeeded)
                return Result.Failure(roleResult.ToErrorMessage());

            _context.TenantUsers.Add(new TenantUser(
                tenantId: tenant.Id,
                userId: user.Id,
                role: TenantUserRole.Owner,
                status: TenantUserStatus.Active));
            // The link goes out from the outbox, after the commit, and is retried if the provider fails. It activates
            // the account (signing in needs a confirmed email or phone); the phone is verified later from the profile.
            _outbox.Add(new AccountEmailRequestedEvent(AccountEmailPurpose.EmailConfirmation, UserId: user.Id), tenantId: null);
            await _context.SaveChangesAsync(ct);

            return Result.Success();
        }, cancellationToken);

        if (registration.IsFailure)
            return Result.Failure<Guid>(registration.Error!);

        return Result.Success(user.Id);
    }
}
