using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Identity.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity.Queries;

[RequiresAuthentication]
public record GetMyTenantsQuery : IRequest<IReadOnlyList<MyTenantDto>>;

public class GetMyTenantsQueryHandler : IRequestHandler<GetMyTenantsQuery, IReadOnlyList<MyTenantDto>>
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IApplicationDbContext _context;

    public GetMyTenantsQueryHandler(ICurrentUserService currentUserService, IApplicationDbContext context)
    {
        _currentUserService = currentUserService;
        _context = context;
    }

    public async Task<IReadOnlyList<MyTenantDto>> Handle(GetMyTenantsQuery request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var userId))
            return [];

        var result = await (
            from tu in _context.TenantUsers.IgnoreQueryFilters()
            join t in _context.Tenants.IgnoreQueryFilters() on tu.TenantId equals t.Id
            where tu.UserId == userId
            select new MyTenantDto
            {
                TenantId = t.Id,
                Name = t.Name,
                Slug = t.Slug,
                Role = tu.Role.ToString(),
                Status = tu.Status.ToString()
            })
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return result;
    }
}
