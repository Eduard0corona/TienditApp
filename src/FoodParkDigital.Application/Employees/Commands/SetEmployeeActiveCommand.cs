using FoodParkDigital.Application.Billing;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Employees.Commands;

[RequiresPermission(TenantPermission.ManageEmployees)]
public record SetEmployeeActiveCommand : IRequest<Result>
{
    public Guid TenantUserId { get; init; }
    public bool IsActive { get; init; }
}

public class SetEmployeeActiveCommandHandler : IRequestHandler<SetEmployeeActiveCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public SetEmployeeActiveCommandHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    public async Task<Result> Handle(SetEmployeeActiveCommand request, CancellationToken cancellationToken)
    {
        if (_currentUserService.Role is not ("TENANT_ADMIN" or "SUPER_ADMIN"))
            return Result.Failure("No tienes permisos para actualizar empleados.");

        var tenantUser = await _context.TenantUsers
            .FirstOrDefaultAsync(
                x => x.Id == request.TenantUserId && x.TenantId == _tenantService.TenantId,
                cancellationToken);

        if (tenantUser is null)
            return Result.Failure("Empleado no encontrado.");

        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == tenantUser.UserId, cancellationToken);
        if (user is null)
            return Result.Failure("Empleado no encontrado.");

        if (request.IsActive)
        {
            // A pending invitation already holds its seat; a disabled person takes one back.
            if (tenantUser.Status == TenantUserStatus.Disabled)
            {
                var planError = await SubscriptionRules.CheckUserLimitAsync(_context, tenantUser.TenantId, cancellationToken);
                if (planError is not null)
                    return Result.Failure(planError);
            }

            // Someone who never chose a password keeps (or gets back) their invitation, not an access they cannot use.
            tenantUser.SetStatus(user.PasswordHash is null ? TenantUserStatus.Invited : TenantUserStatus.Active);
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        if (!Guid.TryParse(_currentUserService.UserId, out var currentUserId))
            return Result.Failure("Usuario no válido.");

        if (currentUserId == tenantUser.UserId)
            return Result.Failure("No puedes desactivar tu propio acceso.");

        if (tenantUser.Role == TenantUserRole.Owner)
            return Result.Failure(EmployeeAccessRules.OwnerIsPermanentMessage);

        // Administering the business comes from the membership (owning another business gives nothing here).
        if (EmployeeAccessRules.IsManagerRole(tenantUser.Role)
            && !await EmployeeAccessRules.HasAnotherActiveManagerAsync(_context, _tenantService.TenantId, tenantUser.Id, cancellationToken))
        {
            return Result.Failure(EmployeeAccessRules.LastManagerMessage);
        }

        tenantUser.SetStatus(TenantUserStatus.Disabled);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
