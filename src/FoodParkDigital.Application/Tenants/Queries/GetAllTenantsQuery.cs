using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Tenants.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Queries;

[RequiresPlatformAdmin]
public record GetAllTenantsQuery : IRequest<PaginatedList<TenantDto>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetAllTenantsQueryHandler : IRequestHandler<GetAllTenantsQuery, PaginatedList<TenantDto>>
{
    private readonly IApplicationDbContext _context;

    public GetAllTenantsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<TenantDto>> Handle(GetAllTenantsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .OrderByDescending(t => t.CreatedAt)
            .Select(TenantMappings.AsDto);

        return await PaginatedList<TenantDto>.CreateAsync(
            query, request.PageNumber, request.PageSize, cancellationToken);
    }
}
