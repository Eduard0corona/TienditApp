using AutoMapper;
using AutoMapper.QueryableExtensions;
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
    private readonly IMapper _mapper;

    public GetAllTenantsQueryHandler(IApplicationDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<PaginatedList<TenantDto>> Handle(GetAllTenantsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .OrderByDescending(t => t.CreatedAt)
            .ProjectTo<TenantDto>(_mapper.ConfigurationProvider);

        return await PaginatedList<TenantDto>.CreateAsync(
            query, request.PageNumber, request.PageSize, cancellationToken);
    }
}
