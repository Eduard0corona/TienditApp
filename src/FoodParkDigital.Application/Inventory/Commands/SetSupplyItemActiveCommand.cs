using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Commands;

public record SetSupplyItemActiveCommand : IRequest<Result>
{
    public Guid Id { get; init; }
    public bool IsActive { get; init; }
}

public class SetSupplyItemActiveCommandHandler : IRequestHandler<SetSupplyItemActiveCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public SetSupplyItemActiveCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(SetSupplyItemActiveCommand request, CancellationToken cancellationToken)
    {
        var entity = await _context.SupplyItems
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("SupplyItem", request.Id);

        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            entity.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanAccessInventory(tenantCapabilities))
            return Result.Failure("El tenant no tiene habilitado el modulo de inventario.");

        entity.SetActive(request.IsActive);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
