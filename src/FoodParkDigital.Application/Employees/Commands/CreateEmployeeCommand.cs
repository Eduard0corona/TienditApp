using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
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

namespace FoodParkDigital.Application.Employees.Commands;

public record CreateEmployeeCommand : IRequest<Result<EmployeeDto>>
{
    public string Email { get; init; } = null!;
    public string Password { get; init; } = null!;
    public string FirstName { get; init; } = null!;
    public string LastName { get; init; } = null!;
    public string? PhoneNumber { get; init; }
    public UserRole AppRole { get; init; } = UserRole.Staff;
    public TenantUserRole TenantRole { get; init; } = TenantUserRole.Staff;
    public List<string> Permissions { get; init; } = [];
}

public class CreateEmployeeCommandValidator : AbstractValidator<CreateEmployeeCommand>
{
    public CreateEmployeeCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PhoneNumber).MaximumLength(30);
        RuleFor(x => x.AppRole)
            .Must(role => role is UserRole.TenantAdmin or UserRole.Staff or UserRole.Kitchen)
            .WithMessage("El rol de aplicacion no es valido para empleados.");
        RuleFor(x => x.TenantRole)
            .Must(role => role is TenantUserRole.Admin or TenantUserRole.Staff)
            .WithMessage("El rol de tenant no es valido para empleados.");
    }
}

public class CreateEmployeeCommandHandler : IRequestHandler<CreateEmployeeCommand, Result<EmployeeDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateEmployeeCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
        _userManager = userManager;
    }

    public async Task<Result<EmployeeDto>> Handle(CreateEmployeeCommand request, CancellationToken cancellationToken)
    {
        if (_currentUserService.Role is not ("TENANT_ADMIN" or "SUPER_ADMIN"))
            return Result.Failure<EmployeeDto>("No tienes permisos para crear empleados.");

        var email = request.Email.Trim();
        var existingUser = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpper(), cancellationToken);

        if (existingUser is not null)
            return Result.Failure<EmployeeDto>("Ya existe un usuario con ese email.");

        var tenantRole = request.AppRole == UserRole.TenantAdmin
            ? TenantUserRole.Admin
            : request.TenantRole;

        var permissions = TenantPermissionCatalog.NormalizeCodes(request.Permissions);
        if (permissions.Count == 0)
        {
            permissions = TenantPermissionCatalog.GetDefaultCodes(request.AppRole, tenantRole);
        }

        var user = new ApplicationUser
        {
            DefaultTenantId = _tenantService.TenantId,
            UserName = email,
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            Role = request.AppRole,
            EmailConfirmed = true,
            IsActive = true
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
            return Result.Failure<EmployeeDto>(errors);
        }

        var roleResult = await _userManager.AddToRoleAsync(user, EmployeeDefinitions.ToIdentityRole(request.AppRole));
        if (!roleResult.Succeeded)
        {
            var errors = string.Join(", ", roleResult.Errors.Select(e => e.Description));
            return Result.Failure<EmployeeDto>(errors);
        }

        var tenantUser = new TenantUser(
            _tenantService.TenantId,
            user.Id,
            tenantRole,
            TenantUserStatus.Active,
            permissions);
        _context.TenantUsers.Add(tenantUser);
        await _context.SaveChangesAsync(cancellationToken);

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
            IsActive = true,
            CreatedAt = tenantUser.CreatedAt,
            Permissions = permissions.ToList()
        });
    }
}
