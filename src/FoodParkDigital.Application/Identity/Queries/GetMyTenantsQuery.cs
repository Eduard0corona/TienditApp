using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Identity.DTOs;
using FoodParkDigital.Application.Tenants.DTOs;
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

        var memberships = await (
            from tu in _context.TenantUsers.IgnoreQueryFilters()
            join t in _context.Tenants.IgnoreQueryFilters() on tu.TenantId equals t.Id
            where tu.UserId == userId
            orderby t.Name
            select new { t.Id, t.Name, t.Slug, tu.Role, tu.Status, t.LogoUrl })
            .ToListAsync(cancellationToken);

        return memberships
            .Select(m => new MyTenantDto
            {
                TenantId = m.Id,
                Name = m.Name,
                Slug = m.Slug,
                Role = m.Role.ToString(),
                Status = m.Status.ToString(),
                LogoUrl = LogoImageUrlsDto.FromBaseUrl(m.LogoUrl)?.Thumbnail
            })
            .ToList();
    }
}
