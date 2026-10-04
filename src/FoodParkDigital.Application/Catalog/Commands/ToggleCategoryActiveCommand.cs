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
public record ToggleCategoryActiveCommand : IRequest<Result>
{
    public Guid Id { get; init; }
    public bool IsActive { get; init; }
    /// <summary>Archiving a category with products moves them here first (another active category).</summary>
    public Guid? MoveProductsToCategoryId { get; init; }
}

public class ToggleCategoryActiveCommandValidator : AbstractValidator<ToggleCategoryActiveCommand>
{
    public ToggleCategoryActiveCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class ToggleCategoryActiveCommandHandler : IRequestHandler<ToggleCategoryActiveCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public ToggleCategoryActiveCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(ToggleCategoryActiveCommand request, CancellationToken cancellationToken)
    {
        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("Category", request.Id);

        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            category.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanManageCatalog(tenantCapabilities))
            return Result.Failure("El negocio no tiene habilitado el módulo de catálogo.");

        if (request.IsActive)
        {
            category.Activate();
        }
        else
        {
            // An archived category leaves the menus: its products must live in another one to stay editable.
            var products = await _context.Products.Where(p => p.CategoryId == category.Id).ToListAsync(cancellationToken);
            if (products.Count > 0)
            {
                if (request.MoveProductsToCategoryId is not { } targetId)
                    return Result.Failure($"La categoría tiene {products.Count} productos: elige a qué categoría pasarlos antes de archivarla.");

                var targetIsValid = targetId != category.Id && await _context.Categories
                    .AnyAsync(c => c.Id == targetId && c.IsActive, cancellationToken);
                if (!targetIsValid)
                    return Result.Failure("Elige otra categoría activa para los productos.");

                foreach (var product in products)
                    product.MoveToCategory(targetId);
            }

            category.Deactivate();
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
