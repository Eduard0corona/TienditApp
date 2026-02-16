using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Infrastructure.Services;

public class TenantAccessService : ITenantAccessService
{
    private readonly IApplicationDbContext _context;

    public TenantAccessService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> HasActiveAccessAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        return await _context.TenantUsers
            .IgnoreQueryFilters()
            .AnyAsync(
                x => x.UserId == userId
                    && x.TenantId == tenantId
                    && x.Status == TenantUserStatus.Active,
                cancellationToken);
    }
}
