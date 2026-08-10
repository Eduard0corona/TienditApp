using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Employees.DTOs;
using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Employees.Queries;

public record GetEmployeesQuery : IRequest<PaginatedList<EmployeeDto>>
{
    public string? Search { get; init; }
    public string? AppRole { get; init; }
    public string? TenantRole { get; init; }
    public string? Status { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetEmployeesQueryHandler : IRequestHandler<GetEmployeesQuery, PaginatedList<EmployeeDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetEmployeesQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PaginatedList<EmployeeDto>> Handle(GetEmployeesQuery request, CancellationToken cancellationToken)
    {
        var query = _context.TenantUsers
            .AsNoTracking()
            .Where(x => x.TenantId == _tenantService.TenantId)
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
            .Where(x => x.User != null && x.User.Role != UserRole.Customer)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                (x.User!.Email != null && x.User.Email.ToLower().Contains(search))
                || x.User.FirstName.ToLower().Contains(search)
                || x.User.LastName.ToLower().Contains(search)
                || (x.User.PhoneNumber != null && x.User.PhoneNumber.ToLower().Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(request.AppRole)
            && Enum.TryParse<UserRole>(request.AppRole, true, out var appRoleFilter))
        {
            query = query.Where(x => x.User!.Role == appRoleFilter);
        }

        if (!string.IsNullOrWhiteSpace(request.TenantRole)
            && Enum.TryParse<TenantUserRole>(request.TenantRole, true, out var tenantRoleFilter))
        {
            query = query.Where(x => x.TenantUser.Role == tenantRoleFilter);
        }

        if (!string.IsNullOrWhiteSpace(request.Status)
            && Enum.TryParse<TenantUserStatus>(request.Status, true, out var statusFilter))
        {
            query = query.Where(x => x.TenantUser.Status == statusFilter);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(x => x.User!.FirstName)
            .ThenBy(x => x.User!.LastName)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(x => new EmployeeDto
        {
            TenantUserId = x.TenantUser.Id,
            UserId = x.User!.Id,
            Email = x.User.Email ?? string.Empty,
            FirstName = x.User.FirstName,
            LastName = x.User.LastName,
            PhoneNumber = x.User.PhoneNumber,
            AppRole = x.User.Role.ToString(),
            TenantRole = x.TenantUser.Role.ToString(),
            Status = x.TenantUser.Status.ToString(),
            IsActive = x.TenantUser.Status == TenantUserStatus.Active && x.User.IsActive,
            CreatedAt = x.TenantUser.CreatedAt,
            Permissions = TenantPermissionCatalog.ResolveEffectiveCodes(
                x.TenantUser.PermissionCodes,
                x.User.Role,
                x.TenantUser.Role).ToList()
        }).ToList();

        return new PaginatedList<EmployeeDto>(items, totalCount, request.PageNumber, request.PageSize);
    }
}
