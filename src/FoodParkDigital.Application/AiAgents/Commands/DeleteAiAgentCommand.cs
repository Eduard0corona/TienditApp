using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.AiAgents.Commands;

public record DeleteAiAgentCommand(Guid Id) : IRequest<Result>;

public class DeleteAiAgentCommandHandler : IRequestHandler<DeleteAiAgentCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public DeleteAiAgentCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(DeleteAiAgentCommand request, CancellationToken cancellationToken)
    {
        var agent = await _context.AiAgents
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("AiAgent", request.Id);

        _context.AiAgents.Remove(agent);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
