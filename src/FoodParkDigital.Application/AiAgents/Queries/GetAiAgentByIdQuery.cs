using FoodParkDigital.Application.AiAgents.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.AiAgents.Queries;

[RequiresPermission(TenantPermission.ManageAiAgent)]
public record GetAiAgentByIdQuery(Guid Id) : IRequest<AiAgentDto>;

public class GetAiAgentByIdQueryHandler : IRequestHandler<GetAiAgentByIdQuery, AiAgentDto>
{
    private readonly IApplicationDbContext _context;

    public GetAiAgentByIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AiAgentDto> Handle(GetAiAgentByIdQuery request, CancellationToken cancellationToken)
    {
        var agent = await _context.AiAgents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("AiAgent", request.Id);

        return agent.ToDto();
    }
}
