using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.AiAgents.Commands;

[RequiresPermission(TenantPermission.ManageAiAgent)]
public record SetAiAgentActiveCommand : IRequest<Result>
{
    public Guid Id { get; init; }
    public bool IsActive { get; init; }
}

public class SetAiAgentActiveCommandHandler : IRequestHandler<SetAiAgentActiveCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public SetAiAgentActiveCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(SetAiAgentActiveCommand request, CancellationToken cancellationToken)
    {
        var agent = await _context.AiAgents
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("AiAgent", request.Id);

        agent.SetActive(request.IsActive);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
