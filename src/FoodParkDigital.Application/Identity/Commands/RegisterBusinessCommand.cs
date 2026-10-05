using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Identity.DTOs;
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

[AllowAnonymousRequest("Acceso público: inicio de sesión, registro y confirmaciones.")]
public record RegisterBusinessCommand : IRequest<Result<AuthResponse>>
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

public class RegisterBusinessCommandHandler : IRequestHandler<RegisterBusinessCommand, Result<AuthResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly ISmsService _smsService;
    private readonly IDateTimeProvider _clock;

    public RegisterBusinessCommandHandler(
        UserManager<ApplicationUser> userManager,
        IJwtTokenService jwtTokenService,
        IApplicationDbContext context,
        IEmailService emailService,
        ISmsService smsService,
        IDateTimeProvider clock)
    {
        _userManager = userManager;
        _jwtTokenService = jwtTokenService;
        _context = context;
        _emailService = emailService;
        _smsService = smsService;
        _clock = clock;
    }

    public async Task<Result<AuthResponse>> Handle(RegisterBusinessCommand request, CancellationToken cancellationToken)
    {
        // Check for duplicate email
        var normalizedEmail = request.Email.ToUpperInvariant();
        var existingUser = await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        if (existingUser)
            return Result.Failure<AuthResponse>("Ya existe una cuenta con este correo electrónico.");

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
            return Result.Failure<AuthResponse>(passwordResult.ToErrorMessage());

        // Tenant, owner, membership and session are committed together or not at all
        var registration = await _context.ExecuteInResilientTransactionAsync(async ct =>
        {
            await BusinessProvisioning.AddAsync(_context, tenant, _clock.UtcNow, ct);
            await _context.SaveChangesAsync(ct);

            var createResult = await _userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
                return Result.Failure<IssuedRefreshToken>(createResult.ToErrorMessage());

            var roleResult = await _userManager.AddToRoleAsync(user, "TENANT_ADMIN");
            if (!roleResult.Succeeded)
                return Result.Failure<IssuedRefreshToken>(roleResult.ToErrorMessage());

            _context.TenantUsers.Add(new TenantUser(
                tenantId: tenant.Id,
                userId: user.Id,
                role: TenantUserRole.Owner,
                status: TenantUserStatus.Active));

            // The first session of the account, started with its registration.
            var sessionToken = _jwtTokenService.GenerateRefreshToken(user.Id, Guid.NewGuid());
            _context.RefreshTokens.Add(sessionToken.Token);
            await _context.SaveChangesAsync(ct);

            return Result.Success(sessionToken);
        }, cancellationToken);

        if (registration.IsFailure)
            return Result.Failure<AuthResponse>(registration.Error!);

        // Send confirmations only after the registration is committed
        var emailToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        await _emailService.SendEmailConfirmationAsync(user.Email!, user.Id.ToString(), emailToken);

        var phoneCode = await _userManager.GenerateChangePhoneNumberTokenAsync(user, request.PhoneNumber);
        await _smsService.SendVerificationCodeAsync(request.PhoneNumber, phoneCode);

        // Auto-login: generate tokens
        var refreshToken = registration.Value;
        var accessToken = _jwtTokenService.GenerateAccessToken(
            user,
            refreshToken.Token.SessionId,
            new TenantTokenContext(tenant.Id, TenantUserRole.Owner.ToString().ToUpperInvariant()));

        return Result.Success(new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Value,
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
