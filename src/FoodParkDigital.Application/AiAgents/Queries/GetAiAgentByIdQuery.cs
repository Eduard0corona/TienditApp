using AutoMapper;
using FoodParkDigital.Application.AiAgents.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.AiAgents.Queries;

public record GetAiAgentByIdQuery(Guid Id) : IRequest<AiAgentDto>;

public class GetAiAgentByIdQueryHandler : IRequestHandler<GetAiAgentByIdQuery, AiAgentDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;

    public GetAiAgentByIdQueryHandler(IApplicationDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<AiAgentDto> Handle(GetAiAgentByIdQuery request, CancellationToken cancellationToken)
    {
        var agent = await _context.AiAgents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("AiAgent", request.Id);

        return _mapper.Map<AiAgentDto>(agent);
    }
}
