using FoodParkDigital.Application.Employees;
using FoodParkDigital.Application.Employees.DTOs;
using MediatR;

namespace FoodParkDigital.Application.Employees.Queries;

public record GetEmployeePermissionOptionsQuery : IRequest<IReadOnlyList<EmployeePermissionOptionDto>>;

public class GetEmployeePermissionOptionsQueryHandler : IRequestHandler<GetEmployeePermissionOptionsQuery, IReadOnlyList<EmployeePermissionOptionDto>>
{
    public Task<IReadOnlyList<EmployeePermissionOptionDto>> Handle(GetEmployeePermissionOptionsQuery request, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<EmployeePermissionOptionDto>>(EmployeeDefinitions.PermissionOptions);
}
