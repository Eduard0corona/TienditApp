using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Application.Tenants.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Queries;

[RequiresAuthentication]
public record GetMyTenantQuery : IRequest<Result<TenantProfileDto>>;

public class GetMyTenantQueryHandler : IRequestHandler<GetMyTenantQuery, Result<TenantProfileDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICurrentTenantService _tenantService;
    private readonly IMapper _mapper;

    public GetMyTenantQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        ICurrentTenantService tenantService,
        IMapper mapper)
    {
        _context = context;
        _currentUserService = currentUserService;
        _tenantService = tenantService;
        _mapper = mapper;
    }

    public async Task<Result<TenantProfileDto>> Handle(GetMyTenantQuery request, CancellationToken cancellationToken)
    {
        var tenantId = await MyTenantResolver.ResolveAsync(
            _context,
            _tenantService,
            _currentUserService,
            cancellationToken);

        if (tenantId is null)
            return Result.Failure<TenantProfileDto>("No se encontro el tenant del usuario.");

        var tenant = await _context.Tenants
            .AsNoTracking()
            .Include(t => t.Settings)
            .FirstOrDefaultAsync(t => t.Id == tenantId.Value, cancellationToken);

        if (tenant is null)
            return Result.Failure<TenantProfileDto>("Tenant no encontrado.");

        var dto = _mapper.Map<TenantProfileDto>(tenant);
        return Result.Success(dto);
    }
}
