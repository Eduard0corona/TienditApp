using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Commands;

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
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Matches(@"^\+[1-9]\d{6,14}$")
            .WithMessage("El número de teléfono debe estar en formato E.164 (ej: +5215512345678).");
    }
}

public class RegisterCustomerCommandHandler : IRequestHandler<RegisterCustomerCommand, Result<AuthResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly ISmsService _smsService;

    public RegisterCustomerCommandHandler(
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

    public async Task<Result<AuthResponse>> Handle(RegisterCustomerCommand request, CancellationToken cancellationToken)
    {
        // Check for duplicate email
        var normalizedEmail = request.Email.ToUpperInvariant();
        var existingUser = await _context.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        if (existingUser)
            return Result.Failure<AuthResponse>("Ya existe una cuenta con este correo electrónico.");

        // Get system tenant (customers belong to the platform tenant)
        var systemTenant = await _context.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Slug == "system", cancellationToken);

        if (systemTenant is null)
            return Result.Failure<AuthResponse>("Error de configuración del sistema.");

        // Create customer user
        var user = new ApplicationUser
        {
            TenantId = systemTenant.Id,
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            Role = UserRole.Customer,
            EmailConfirmed = false,
            IsActive = true
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
            return Result.Failure<AuthResponse>(errors);
        }

        await _userManager.AddToRoleAsync(user, "CUSTOMER");

        // Send confirmations
        var emailToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        await _emailService.SendEmailConfirmationAsync(user.Email!, user.Id.ToString(), emailToken);

        var phoneCode = await _userManager.GenerateChangePhoneNumberTokenAsync(user, request.PhoneNumber);
        await _smsService.SendVerificationCodeAsync(request.PhoneNumber, phoneCode);

        // Auto-login: generate tokens
        var accessToken = _jwtTokenService.GenerateAccessToken(user);
        var refreshToken = _jwtTokenService.GenerateRefreshToken(user.Id);

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync(cancellationToken);

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
                TenantId = user.TenantId,
                IsActive = user.IsActive,
                AvatarUrl = null,
                AvatarImages = null
            }
        });
    }
}
