using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Commands;

[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record SetInventoryLocationActiveCommand : IRequest<Result>
{
    public Guid Id { get; init; }
    public bool IsActive { get; init; }
}

public class SetInventoryLocationActiveCommandHandler : IRequestHandler<SetInventoryLocationActiveCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public SetInventoryLocationActiveCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(SetInventoryLocationActiveCommand request, CancellationToken cancellationToken)
    {
        var entity = await _context.InventoryLocations
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("InventoryLocation", request.Id);

        if (!request.IsActive && entity.IsDefault)
            return Result.Failure("No puedes desactivar la ubicación por defecto.");

        entity.SetActive(request.IsActive);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
