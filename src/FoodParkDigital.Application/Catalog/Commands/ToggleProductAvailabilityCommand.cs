using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Catalog.Commands;

[RequiresPermission(TenantPermission.ManageCatalog)]
public record ToggleProductAvailabilityCommand : IRequest<Result>
{
    public Guid Id { get; init; }
    public bool IsAvailable { get; init; }
}

public class ToggleProductAvailabilityCommandValidator : AbstractValidator<ToggleProductAvailabilityCommand>
{
    public ToggleProductAvailabilityCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class ToggleProductAvailabilityCommandHandler : IRequestHandler<ToggleProductAvailabilityCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public ToggleProductAvailabilityCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(ToggleProductAvailabilityCommand request, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("Product", request.Id);

        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            product.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanManageProductUnit(tenantCapabilities, product.Unit))
        {
            return Result.Failure("El negocio no tiene habilitado el módulo requerido para este tipo de artículo.");
        }

        product.SetAvailability(request.IsAvailable);
        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
