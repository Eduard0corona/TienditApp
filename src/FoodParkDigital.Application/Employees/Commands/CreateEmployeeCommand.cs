using FoodParkDigital.Application.Billing;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Employees.DTOs;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Employees.Commands;

/// <summary>
/// Adds a person to the team. Someone new receives an invitation to choose their own password (the response carries
/// the link to share it by WhatsApp); someone who already has an account joins with it, without touching it.
/// </summary>
[RequiresTenantManager]
public record CreateEmployeeCommand : IRequest<Result<EmployeeDto>>
{
    public string Email { get; init; } = null!;
    /// <summary>Optional, for API callers that still set one; the app always invites.</summary>
    public string? Password { get; init; }
    public string FirstName { get; init; } = null!;
    public string LastName { get; init; } = null!;
    public string? PhoneNumber { get; init; }
    public UserRole AppRole { get; init; } = UserRole.Staff;
    public TenantUserRole TenantRole { get; init; } = TenantUserRole.Staff;
    /// <summary>Null takes the defaults of the role; an empty list is "sin acceso".</summary>
    public List<string>? Permissions { get; init; }
}

public class CreateEmployeeCommandValidator : AbstractValidator<CreateEmployeeCommand>
{
    public CreateEmployeeCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).MinimumLength(6).When(x => !string.IsNullOrEmpty(x.Password));
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PhoneNumber).MaximumLength(30);
        RuleFor(x => x.AppRole)
            .Must(role => role is UserRole.TenantAdmin or UserRole.Staff or UserRole.Kitchen)
            .WithMessage("El rol de aplicación no es válido para empleados.");
        RuleFor(x => x.TenantRole)
            .Must(role => role is TenantUserRole.Admin or TenantUserRole.Staff)
            .WithMessage("El rol en el negocio no es válido para empleados.");
    }
}

public class CreateEmployeeCommandHandler : IRequestHandler<CreateEmployeeCommand, Result<EmployeeDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService? _emailService;
    private readonly ILogger<CreateEmployeeCommandHandler> _logger;

    public CreateEmployeeCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        UserManager<ApplicationUser> userManager,
        IEmailService? emailService = null,
        ILogger<CreateEmployeeCommandHandler>? logger = null)
    {
        _context = context;
        _tenantService = tenantService;
        _userManager = userManager;
        _emailService = emailService;
        _logger = logger ?? NullLogger<CreateEmployeeCommandHandler>.Instance;
    }

    public async Task<Result<EmployeeDto>> Handle(CreateEmployeeCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        var email = request.Email.Trim();
        // Accounts are shared by every business of a person.
        var existingUser = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpper(), cancellationToken);

        if (existingUser is not null)
        {
            if (existingUser.Role is UserRole.Customer)
                return Result.Failure<EmployeeDto>("Ese correo es de una cuenta de cliente: pídele otro correo para el equipo.");
            if (existingUser.Role is UserRole.SuperAdmin)
                return Result.Failure<EmployeeDto>("Esa cuenta no se puede agregar al equipo.");

            var membership = await _context.TenantUsers
                .FirstOrDefaultAsync(x => x.UserId == existingUser.Id, cancellationToken);
            if (membership is not null)
            {
                return Result.Failure<EmployeeDto>(membership.Status == TenantUserStatus.Disabled
                    ? "Ya está en el equipo, desactivado: actívalo desde la lista."
                    : "Ya es parte del equipo.");
            }
        }

        var planError = await SubscriptionRules.CheckUserLimitAsync(_context, tenantId, cancellationToken);
        if (planError is not null)
            return Result.Failure<EmployeeDto>(planError);

        var tenantRole = request.AppRole == UserRole.TenantAdmin
            ? TenantUserRole.Admin
            : request.TenantRole;

        var user = existingUser;
        // Someone already invited elsewhere who has not chosen a password yet is invited here too.
        var invite = user is not null && !await _userManager.HasPasswordAsync(user);
        if (user is null)
        {
            user = new ApplicationUser
            {
                DefaultTenantId = tenantId,
                UserName = email,
                Email = email,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
                Role = request.AppRole,
                // Confirmed when the person accepts the invitation (or right away with a password set by the API).
                EmailConfirmed = !string.IsNullOrEmpty(request.Password),
                IsActive = true
            };

            invite = string.IsNullOrEmpty(request.Password);
            var createResult = invite
                ? await _userManager.CreateAsync(user)
                : await _userManager.CreateAsync(user, request.Password!);
            if (!createResult.Succeeded)
                return Result.Failure<EmployeeDto>(string.Join(", ", createResult.Errors.Select(e => e.Description)));

            var roleResult = await _userManager.AddToRoleAsync(user, EmployeeDefinitions.ToIdentityRole(request.AppRole));
            if (!roleResult.Succeeded)
                return Result.Failure<EmployeeDto>(string.Join(", ", roleResult.Errors.Select(e => e.Description)));
        }

        var tenantUser = new TenantUser(
            tenantId,
            user.Id,
            tenantRole,
            invite ? TenantUserStatus.Invited : TenantUserStatus.Active);
        if (request.Permissions is null)
            tenantUser.SetPermissionCodes(TenantPermissionCatalog.GetDefaultCodes(request.AppRole, tenantRole));
        else
            tenantUser.SetExplicitPermissions(request.Permissions);
        _context.TenantUsers.Add(tenantUser);
        await _context.SaveChangesAsync(cancellationToken);

        var invitationPath = invite
            ? await EmployeeInvitations.IssueAsync(_userManager, _emailService, _context, user, tenantId, _logger, cancellationToken)
            : null;

        return Result.Success(new EmployeeDto
        {
            TenantUserId = tenantUser.Id,
            UserId = user.Id,
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            PhoneNumber = user.PhoneNumber,
            AppRole = user.Role.ToString(),
            TenantRole = tenantUser.Role.ToString(),
            Status = tenantUser.Status.ToString(),
            IsActive = tenantUser.Status == TenantUserStatus.Active,
            CreatedAt = tenantUser.CreatedAt,
            Permissions = TenantPermissionCatalog.ResolveEffectiveCodes(tenantUser.PermissionCodes, user.Role, tenantRole).ToList(),
            InvitationUrl = invitationPath
        });
    }
}
