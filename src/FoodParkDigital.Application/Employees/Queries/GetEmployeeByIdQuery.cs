using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Employees.DTOs;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Employees.Queries;

public record GetEmployeeByIdQuery(Guid TenantUserId) : IRequest<Result<EmployeeDto>>;

public class GetEmployeeByIdQueryHandler : IRequestHandler<GetEmployeeByIdQuery, Result<EmployeeDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetEmployeeByIdQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<Result<EmployeeDto>> Handle(GetEmployeeByIdQuery request, CancellationToken cancellationToken)
    {
        var row = await _context.TenantUsers
            .AsNoTracking()
            .Where(x => x.Id == request.TenantUserId && x.TenantId == _tenantService.TenantId)
            .Select(x => new
            {
                TenantUser = x,
                User = _context.Users
                    .IgnoreQueryFilters()
                    .Where(u => u.Id == x.UserId)
                    .Select(u => new
                    {
                        u.Id,
                        u.Email,
                        u.FirstName,
                        u.LastName,
                        u.PhoneNumber,
                        u.Role,
                        u.IsActive
                    })
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row?.User is null || row.User.Role == UserRole.Customer)
            return Result.Failure<EmployeeDto>("Empleado no encontrado.");

        var dto = new EmployeeDto
        {
            TenantUserId = row.TenantUser.Id,
            UserId = row.User.Id,
            Email = row.User.Email ?? string.Empty,
            FirstName = row.User.FirstName,
            LastName = row.User.LastName,
            PhoneNumber = row.User.PhoneNumber,
            AppRole = row.User.Role.ToString(),
            TenantRole = row.TenantUser.Role.ToString(),
            Status = row.TenantUser.Status.ToString(),
            IsActive = row.TenantUser.Status == Domain.Tenants.Enums.TenantUserStatus.Active && row.User.IsActive,
            CreatedAt = row.TenantUser.CreatedAt,
            Permissions = TenantPermissionCatalog.ResolveEffectiveCodes(
                row.TenantUser.PermissionCodes,
                row.User.Role,
                row.TenantUser.Role).ToList()
        };

        return Result.Success(dto);
    }
}
