using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Tenants;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

/// <summary>
/// Signs up a business with its owner and sends the link that confirms the owner's email. It opens no session:
/// the account is activated from that link and the owner then signs in with their credentials. Returns the id of
/// the new account.
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
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Matches(@"^\+[1-9]\d{6,14}$")
            .WithMessage("El número de teléfono debe estar en formato E.164 (ej: +5215512345678).");

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
    private readonly IEmailService _emailService;
    private readonly IDateTimeProvider _clock;

    public RegisterBusinessCommandHandler(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext context,
        IEmailService emailService,
        IDateTimeProvider clock)
    {
        _userManager = userManager;
        _context = context;
        _emailService = emailService;
        _clock = clock;
    }

    public async Task<Result<Guid>> Handle(RegisterBusinessCommand request, CancellationToken cancellationToken)
    {
        // Check for duplicate email
        var normalizedEmail = request.Email.ToUpperInvariant();
        var existingUser = await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        if (existingUser)
            return Result.Failure<Guid>("Ya existe una cuenta con este correo electrónico.");

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

        // Create user as TenantAdmin
        var user = new ApplicationUser
        {
            DefaultTenantId = tenant.Id,
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            Role = UserRole.TenantAdmin,
            EmailConfirmed = false,
            IsActive = true
        };

        // Identity checks the password only inside CreateAsync; reject it before writing anything
        var passwordResult = await _userManager.ValidatePasswordPolicyAsync(user, request.Password);
        if (!passwordResult.Succeeded)
            return Result.Failure<Guid>(passwordResult.ToErrorMessage());

        // Tenant, owner and membership are committed together or not at all
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
            await _context.SaveChangesAsync(ct);

            return Result.Success();
        }, cancellationToken);

        if (registration.IsFailure)
            return Result.Failure<Guid>(registration.Error!);

        // The link goes out only after the registration is committed. It activates the account (signing in needs a
        // confirmed email or phone); the phone is verified later from the profile.
        var emailToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        await _emailService.SendEmailConfirmationAsync(user.Email!, user.Id.ToString(), emailToken);

        return Result.Success(user.Id);
    }
}
