using AutoMapper;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Tenants.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tenants.Queries;

public record GetMyTenantQuery : IRequest<Result<TenantProfileDto>>;

public class GetMyTenantQueryHandler : IRequestHandler<GetMyTenantQuery, Result<TenantProfileDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;

    public GetMyTenantQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IMapper mapper)
    {
        _context = context;
        _currentUserService = currentUserService;
        _mapper = mapper;
    }

    public async Task<Result<TenantProfileDto>> Handle(GetMyTenantQuery request, CancellationToken cancellationToken)
    {
        var tenantId = _currentUserService.TenantId;
        var hasUserId = Guid.TryParse(_currentUserService.UserId, out var userId);

        if (hasUserId)
        {
            if (tenantId is null)
            {
                tenantId = await _context.Users
                    .IgnoreQueryFilters()
                    .Where(u => u.Id == userId)
                    .Select(u => u.DefaultTenantId)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (tenantId is null)
            {
                tenantId = await _context.TenantUsers
                    .IgnoreQueryFilters()
                    .Where(tu => tu.UserId == userId)
                    .OrderBy(tu => tu.CreatedAt)
                    .Select(tu => (Guid?)tu.TenantId)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        if (tenantId is null)
            return Result.Failure<TenantProfileDto>("No se encontro el tenant del usuario.");

        var tenant = await _context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId.Value, cancellationToken);

        if (tenant is null && hasUserId)
        {
            tenant = await (
                from tu in _context.TenantUsers.IgnoreQueryFilters()
                join t in _context.Tenants.IgnoreQueryFilters() on tu.TenantId equals t.Id
                where tu.UserId == userId
                orderby tu.CreatedAt
                select t)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (tenant is null)
            return Result.Failure<TenantProfileDto>("Tenant no encontrado.");

        var dto = _mapper.Map<TenantProfileDto>(tenant);
        return Result.Success(dto);
    }
}
