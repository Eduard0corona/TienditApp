using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace FoodParkDigital.Application.Identity.Commands;

public record RegisterUserCommand : IRequest<Result<UserDto>>
{
    public string Email { get; init; } = null!;
    public string Password { get; init; } = null!;
    public string FirstName { get; init; } = null!;
    public string LastName { get; init; } = null!;
    public string PhoneNumber { get; init; } = null!;
    public UserRole Role { get; init; }
}

public class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Matches(@"^\+[1-9]\d{6,14}$")
            .WithMessage("El número de teléfono debe estar en formato E.164 (ej: +5215512345678).");
    }
}

public class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, Result<UserDto>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentTenantService _tenantService;
    private readonly IApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly ISmsService _smsService;

    public RegisterUserCommandHandler(
        UserManager<ApplicationUser> userManager,
        ICurrentTenantService tenantService,
        IApplicationDbContext context,
        IEmailService emailService,
        ISmsService smsService)
    {
        _userManager = userManager;
        _tenantService = tenantService;
        _context = context;
        _emailService = emailService;
        _smsService = smsService;
    }

    public async Task<Result<UserDto>> Handle(RegisterUserCommand request, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            DefaultTenantId = _tenantService.TenantId,
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            Role = request.Role,
            EmailConfirmed = false,
            IsActive = true
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return Result.Failure<UserDto>(errors);
        }

        var roleName = request.Role switch
        {
            UserRole.SuperAdmin => "SUPER_ADMIN",
            UserRole.TenantAdmin => "TENANT_ADMIN",
            UserRole.Staff => "STAFF",
            UserRole.Kitchen => "KITCHEN",
            UserRole.Customer => "CUSTOMER",
            _ => "STAFF"
        };
        await _userManager.AddToRoleAsync(user, roleName);

        var tenantUserRole = request.Role == UserRole.TenantAdmin ? TenantUserRole.Admin : TenantUserRole.Staff;
        _context.TenantUsers.Add(new TenantUser(_tenantService.TenantId, user.Id, tenantUserRole, TenantUserStatus.Active));
        await _context.SaveChangesAsync(cancellationToken);

        // Send email confirmation
        var emailToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        await _emailService.SendEmailConfirmationAsync(user.Email!, user.Id.ToString(), emailToken);

        // Send SMS verification
        var phoneCode = await _userManager.GenerateChangePhoneNumberTokenAsync(user, request.PhoneNumber);
        await _smsService.SendVerificationCodeAsync(request.PhoneNumber, phoneCode);

        return Result.Success(new UserDto
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
        });
    }
}
