using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Commands;

public record UpsertProductRecipeLineRequest
{
    public Guid? RequirementId { get; init; }
    public Guid SupplyItemId { get; init; }
    public Guid? InventoryLocationId { get; init; }
    public decimal Quantity { get; init; }
    public decimal WastePercent { get; init; }
    public string? Notes { get; init; }
}

[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record UpsertProductRecipeCommand : IRequest<Result<ProductRecipeDto>>
{
    public Guid ProductId { get; init; }
    public List<UpsertProductRecipeLineRequest> Lines { get; init; } = [];
}

public class UpsertProductRecipeCommandValidator : AbstractValidator<UpsertProductRecipeCommand>
{
    public UpsertProductRecipeCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.SupplyItemId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.WastePercent).InclusiveBetween(0, 100);
            line.RuleFor(x => x.Notes).MaximumLength(500);
        });
    }
}

public class UpsertProductRecipeCommandHandler : IRequestHandler<UpsertProductRecipeCommand, Result<ProductRecipeDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public UpsertProductRecipeCommandHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<Result<ProductRecipeDto>> Handle(UpsertProductRecipeCommand request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanAccessInventory(tenantCapabilities))
            return Result.Failure<ProductRecipeDto>("El tenant no tiene habilitado el modulo de inventario.");

        var product = await _context.Products
            .FirstOrDefaultAsync(x => x.Id == request.ProductId, cancellationToken)
            ?? throw new EntityNotFoundException("Product", request.ProductId);

        var duplicatedLines = request.Lines
            .GroupBy(x => new { x.SupplyItemId, x.InventoryLocationId })
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicatedLines.Count > 0)
            return Result.Failure<ProductRecipeDto>("No puedes repetir el mismo insumo y ubicacion en la receta.");

        var supplyIds = request.Lines.Select(x => x.SupplyItemId).Distinct().ToList();
        var locationIds = request.Lines
            .Where(x => x.InventoryLocationId.HasValue)
            .Select(x => x.InventoryLocationId!.Value)
            .Distinct()
            .ToList();

        if (supplyIds.Count > 0)
        {
            var existingSupplyCount = await _context.SupplyItems
                .Where(x => supplyIds.Contains(x.Id) && x.IsActive)
                .CountAsync(cancellationToken);
            if (existingSupplyCount != supplyIds.Count)
                return Result.Failure<ProductRecipeDto>("Uno o mas insumos no existen o estan inactivos.");
        }

        if (locationIds.Count > 0)
        {
            var existingLocationCount = await _context.InventoryLocations
                .Where(x => locationIds.Contains(x.Id) && x.IsActive)
                .CountAsync(cancellationToken);
            if (existingLocationCount != locationIds.Count)
                return Result.Failure<ProductRecipeDto>("Una o mas ubicaciones no existen o estan inactivas.");
        }

        var existingRequirements = await _context.ProductSupplyRequirements
            .Where(x => x.ProductId == request.ProductId)
            .ToListAsync(cancellationToken);

        var incomingIds = request.Lines
            .Where(x => x.RequirementId.HasValue)
            .Select(x => x.RequirementId!.Value)
            .ToHashSet();

        var toDelete = existingRequirements
            .Where(x => !incomingIds.Contains(x.Id))
            .ToList();
        if (toDelete.Count > 0)
            _context.ProductSupplyRequirements.RemoveRange(toDelete);

        var existingById = existingRequirements.ToDictionary(x => x.Id);
        foreach (var line in request.Lines)
        {
            var existing = line.RequirementId.HasValue
                ? existingById.GetValueOrDefault(line.RequirementId.Value)
                : null;
            if (existing is not null && existing.SupplyItemId == line.SupplyItemId)
            {
                existing.Update(
                    quantity: line.Quantity,
                    inventoryLocationId: line.InventoryLocationId,
                    wastePercent: line.WastePercent,
                    notes: line.Notes?.Trim());
                existing.SetActive(true);
                continue;
            }

            // A line whose supply changed is a different requirement: it replaces the old one. Before,
            // the new supply was silently ignored and the old one kept being consumed.
            if (existing is not null)
                _context.ProductSupplyRequirements.Remove(existing);

            _context.ProductSupplyRequirements.Add(new ProductSupplyRequirement(
                tenantId: _tenantService.TenantId,
                productId: request.ProductId,
                supplyItemId: line.SupplyItemId,
                quantity: line.Quantity,
                inventoryLocationId: line.InventoryLocationId,
                wastePercent: line.WastePercent,
                notes: line.Notes?.Trim()));
        }

        await _context.SaveChangesAsync(cancellationToken);

        var dto = await BuildRecipeDtoAsync(product.Id, cancellationToken);
        return Result.Success(dto);
    }

    private async Task<ProductRecipeDto> BuildRecipeDtoAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Where(x => x.Id == productId)
            .Select(x => new { x.Id, x.Name, x.IsAvailable })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("Product", productId);

        var lines = await (
            from req in _context.ProductSupplyRequirements.AsNoTracking()
            join supply in _context.SupplyItems.AsNoTracking() on req.SupplyItemId equals supply.Id
            join location in _context.InventoryLocations.AsNoTracking() on req.InventoryLocationId equals location.Id into locationJoin
            from location in locationJoin.DefaultIfEmpty()
            where req.ProductId == productId
            orderby supply.Name
            select new ProductRecipeLineDto
            {
                RequirementId = req.Id,
                SupplyItemId = req.SupplyItemId,
                SupplyItemName = supply.Name,
                SupplyCode = supply.Code,
                InventoryLocationId = req.InventoryLocationId,
                InventoryLocationName = location != null ? location.Name : null,
                Quantity = req.Quantity,
                WastePercent = req.WastePercent,
                EffectiveQuantity = req.Quantity * (1 + (req.WastePercent / 100m)),
                Notes = req.Notes,
                IsActive = req.IsActive
            })
            .ToListAsync(cancellationToken);

        return new ProductRecipeDto
        {
            ProductId = product.Id,
            ProductName = product.Name,
            ProductIsAvailable = product.IsAvailable,
            Lines = lines
        };
    }
}
