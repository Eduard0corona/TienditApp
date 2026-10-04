using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Branches;

/// <summary>Branches of the business, the default one first. Every member sees them to pick where they work.</summary>
[RequiresTenantMember]
public record GetBranchesQuery : IRequest<List<BranchDto>>
{
    public bool? IsActive { get; init; }
}

public class GetBranchesQueryHandler : IRequestHandler<GetBranchesQuery, List<BranchDto>>
{
    private readonly IApplicationDbContext _context;

    public GetBranchesQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<BranchDto>> Handle(GetBranchesQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Branches.AsNoTracking();
        if (request.IsActive is { } isActive)
            query = query.Where(x => x.IsActive == isActive);

        return await query
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(BranchDto.Projection)
            .ToListAsync(cancellationToken);
    }
}
