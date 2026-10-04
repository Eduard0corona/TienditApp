using AutoMapper;
using AutoMapper.QueryableExtensions;
using FoodParkDigital.Application.AiAgents.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.AiAgents.Queries;

[RequiresPermission(TenantPermission.ManageAiAgent)]
public record GetAiAgentsQuery : IRequest<List<AiAgentDto>>
{
    public bool? IsActive { get; init; }
}

public class GetAiAgentsQueryHandler : IRequestHandler<GetAiAgentsQuery, List<AiAgentDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;

    public GetAiAgentsQueryHandler(IApplicationDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<List<AiAgentDto>> Handle(GetAiAgentsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.AiAgents
            .AsNoTracking()
            .AsQueryable();

        if (request.IsActive.HasValue)
            query = query.Where(x => x.IsActive == request.IsActive.Value);

        return await query
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .ProjectTo<AiAgentDto>(_mapper.ConfigurationProvider)
            .ToListAsync(cancellationToken);
    }
}
