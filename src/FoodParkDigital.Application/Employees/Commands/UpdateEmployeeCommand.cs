using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Employees.DTOs;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Employees.Commands;

[RequiresPermission(TenantPermission.ManageEmployees)]
public record UpdateEmployeeCommand : IRequest<Result<EmployeeDto>>
{
    public Guid TenantUserId { get; init; }
    public string FirstName { get; init; } = null!;
    public string LastName { get; init; } = null!;
    public string? PhoneNumber { get; init; }
    public UserRole AppRole { get; init; } = UserRole.Staff;
    public TenantUserRole TenantRole { get; init; } = TenantUserRole.Staff;
    public List<string> Permissions { get; init; } = [];
}

public class UpdateEmployeeCommandValidator : AbstractValidator<UpdateEmployeeCommand>
{
    public UpdateEmployeeCommandValidator()
    {
        RuleFor(x => x.TenantUserId).NotEmpty();
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

public class UpdateEmployeeCommandHandler : IRequestHandler<UpdateEmployeeCommand, Result<EmployeeDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly UserManager<ApplicationUser> _userManager;

    public UpdateEmployeeCommandHandler(
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

    public async Task<Result<EmployeeDto>> Handle(UpdateEmployeeCommand request, CancellationToken cancellationToken)
    {
        if (_currentUserService.Role is not ("TENANT_ADMIN" or "SUPER_ADMIN"))
            return Result.Failure<EmployeeDto>("No tienes permisos para editar empleados.");

        var tenantUser = await _context.TenantUsers
            .FirstOrDefaultAsync(
                x => x.Id == request.TenantUserId && x.TenantId == _tenantService.TenantId,
                cancellationToken);

        if (tenantUser is null)
            return Result.Failure<EmployeeDto>("Empleado no encontrado.");

        var user = await _userManager.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == tenantUser.UserId, cancellationToken);

        if (user is null || user.Role == UserRole.Customer)
            return Result.Failure<EmployeeDto>("Empleado no encontrado.");

        // The owner keeps the business: from here only their name and phone change, never their role.
        var isOwner = tenantUser.Role == TenantUserRole.Owner;
        var appRole = isOwner ? user.Role : request.AppRole;
        var tenantRole = isOwner
            ? TenantUserRole.Owner
            : request.AppRole == UserRole.TenantAdmin ? TenantUserRole.Admin : request.TenantRole;

        if (EmployeeAccessRules.IsManagerRole(tenantUser.Role) && !EmployeeAccessRules.IsManagerRole(tenantRole))
        {
            if (Guid.TryParse(_currentUserService.UserId, out var currentUserId) && currentUserId == tenantUser.UserId)
                return Result.Failure<EmployeeDto>(EmployeeAccessRules.SelfDemotionMessage);
            if (!await EmployeeAccessRules.HasAnotherActiveManagerAsync(_context, _tenantService.TenantId, tenantUser.Id, cancellationToken))
                return Result.Failure<EmployeeDto>(EmployeeAccessRules.LastManagerMessage);
        }

        var permissions = TenantPermissionCatalog.NormalizeCodes(request.Permissions);
        if (permissions.Count == 0)
        {
            permissions = TenantPermissionCatalog.GetDefaultCodes(appRole, tenantRole);
        }

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();

        // The account role is shared by every business of the person: with another business, only this membership
        // changes (the role inside each business comes from its membership).
        var belongsToAnotherBusiness = user.Role != appRole && await _context.TenantUsers
            .IgnoreQueryFilters()
            .AnyAsync(x => x.UserId == user.Id && x.TenantId != _tenantService.TenantId, cancellationToken);

        if (user.Role != appRole && !belongsToAnotherBusiness)
        {
            user.Role = appRole;
            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Count > 0)
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!removeResult.Succeeded)
                {
                    var removeErrors = string.Join(", ", removeResult.Errors.Select(e => e.Description));
                    return Result.Failure<EmployeeDto>(removeErrors);
                }
            }

            var addRoleResult = await _userManager.AddToRoleAsync(user, EmployeeDefinitions.ToIdentityRole(appRole));
            if (!addRoleResult.Succeeded)
            {
                var addErrors = string.Join(", ", addRoleResult.Errors.Select(e => e.Description));
                return Result.Failure<EmployeeDto>(addErrors);
            }
        }

        tenantUser.SetRole(tenantRole);
        tenantUser.SetPermissionCodes(permissions);

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
            IsActive = tenantUser.Status == TenantUserStatus.Active && user.IsActive,
            CreatedAt = tenantUser.CreatedAt,
            Permissions = permissions.ToList()
        });
    }
}
