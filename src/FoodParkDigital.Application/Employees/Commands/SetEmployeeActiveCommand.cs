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

        if (request.IsActive)
        {
            if (tenantUser.Status != TenantUserStatus.Active)
            {
                var planError = await SubscriptionRules.CheckUserLimitAsync(_context, tenantUser.TenantId, cancellationToken);
                if (planError is not null)
                    return Result.Failure(planError);
            }

            tenantUser.SetStatus(TenantUserStatus.Active);
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == tenantUser.UserId, cancellationToken);
        if (user is null)
            return Result.Failure("Empleado no encontrado.");

        if (!Guid.TryParse(_currentUserService.UserId, out var currentUserId))
            return Result.Failure("Usuario no valido.");

        if (currentUserId == tenantUser.UserId)
            return Result.Failure("No puedes desactivar tu propio acceso.");

        if (tenantUser.Role is TenantUserRole.Owner or TenantUserRole.Admin || user.Role == UserRole.TenantAdmin)
        {
            var activeAdmins = await _context.TenantUsers
                .IgnoreQueryFilters()
                .Where(x =>
                    x.TenantId == _tenantService.TenantId
                    && x.Status == TenantUserStatus.Active)
                .Join(
                    _context.Users.IgnoreQueryFilters(),
                    tu => tu.UserId,
                    u => u.Id,
                    (tu, u) => new { tu, u })
                .CountAsync(
                    x =>
                        (x.tu.Role == TenantUserRole.Owner || x.tu.Role == TenantUserRole.Admin)
                        || x.u.Role == UserRole.TenantAdmin,
                    cancellationToken);

            if (activeAdmins <= 1)
                return Result.Failure("Debe existir al menos un administrador activo en el tenant.");
        }

        tenantUser.SetStatus(TenantUserStatus.Disabled);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
