using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Employees;
using FoodParkDigital.Application.Employees.DTOs;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;

namespace FoodParkDigital.Application.Employees.Queries;

[RequiresPermission(TenantPermission.ManageEmployees)]
public record GetEmployeePermissionOptionsQuery : IRequest<IReadOnlyList<EmployeePermissionOptionDto>>;

public class GetEmployeePermissionOptionsQueryHandler : IRequestHandler<GetEmployeePermissionOptionsQuery, IReadOnlyList<EmployeePermissionOptionDto>>
{
    public Task<IReadOnlyList<EmployeePermissionOptionDto>> Handle(GetEmployeePermissionOptionsQuery request, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<EmployeePermissionOptionDto>>(EmployeeDefinitions.PermissionOptions);
}

[RequiresPermission(TenantPermission.ManageEmployees)]
public record GetEmployeeRolePresetsQuery : IRequest<IReadOnlyList<EmployeeRolePresetDto>>;

public class GetEmployeeRolePresetsQueryHandler : IRequestHandler<GetEmployeeRolePresetsQuery, IReadOnlyList<EmployeeRolePresetDto>>
{
    public Task<IReadOnlyList<EmployeeRolePresetDto>> Handle(GetEmployeeRolePresetsQuery request, CancellationToken cancellationToken)
        => Task.FromResult(EmployeeDefinitions.RolePresets);
}
