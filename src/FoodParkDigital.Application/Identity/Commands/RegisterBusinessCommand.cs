using System.Text.RegularExpressions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

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
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6);
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

    public RegisterBusinessCommandHandler(
        UserManager<ApplicationUser> userManager,
        IJwtTokenService jwtTokenService,
        IApplicationDbContext context,
        IEmailService emailService,
        ISmsService smsService)
    {
        _userManager = userManager;
        _jwtTokenService = jwtTokenService;
        _context = context;
        _emailService = emailService;
        _smsService = smsService;
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

        // Generate slug from business name
        var slug = GenerateSlug(request.BusinessName);
        var slugExists = await _context.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => t.Slug == slug, cancellationToken);

        if (slugExists)
            slug = $"{slug}-{Guid.NewGuid().ToString()[..6]}";

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

        // Create tenant settings with defaults
        var capabilities = TenantCapabilityDefaults.FromBusinessType(request.BusinessType);
        var settings = new TenantSettings(tenant.Id, capabilities);

        var publicGeneralCustomer = new Customer(
            tenantId: tenant.Id,
            name: "Publico en general",
            phone: null,
            email: null,
            isPublicGeneral: true);

        var defaultLocation = new InventoryLocation(
            tenantId: tenant.Id,
            name: "Almacen principal",
            code: "MAIN",
            isDefault: true);

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
            _context.Tenants.Add(tenant);
            _context.TenantSettings.Add(settings);
            _context.Customers.Add(publicGeneralCustomer);
            _context.InventoryLocations.Add(defaultLocation);
            await _context.SaveChangesAsync(ct);

            var createResult = await _userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
                return Result.Failure<RefreshToken>(createResult.ToErrorMessage());

            var roleResult = await _userManager.AddToRoleAsync(user, "TENANT_ADMIN");
            if (!roleResult.Succeeded)
                return Result.Failure<RefreshToken>(roleResult.ToErrorMessage());

            _context.TenantUsers.Add(new TenantUser(
                tenantId: tenant.Id,
                userId: user.Id,
                role: TenantUserRole.Owner,
                status: TenantUserStatus.Active));

            var sessionToken = _jwtTokenService.GenerateRefreshToken(user.Id);
            _context.RefreshTokens.Add(sessionToken);
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
        var accessToken = _jwtTokenService.GenerateAccessToken(
            user,
            new TenantTokenContext(tenant.Id, TenantUserRole.Owner.ToString().ToUpperInvariant()));
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

    private static string GenerateSlug(string name)
    {
        var slug = name.ToLowerInvariant().Trim();
        slug = Regex.Replace(slug, @"[áàäâ]", "a");
        slug = Regex.Replace(slug, @"[éèëê]", "e");
        slug = Regex.Replace(slug, @"[íìïî]", "i");
        slug = Regex.Replace(slug, @"[óòöô]", "o");
        slug = Regex.Replace(slug, @"[úùüû]", "u");
        slug = Regex.Replace(slug, @"[ñ]", "n");
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"[\s]+", "-");
        slug = Regex.Replace(slug, @"-+", "-");
        slug = slug.Trim('-');
        return slug;
    }
}
