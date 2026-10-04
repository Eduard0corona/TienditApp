using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory.Queries;

[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record GetProductRecipeQuery(Guid ProductId) : IRequest<ProductRecipeDto>;

public class GetProductRecipeQueryHandler : IRequestHandler<GetProductRecipeQuery, ProductRecipeDto>
{
    private readonly IApplicationDbContext _context;

    public GetProductRecipeQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ProductRecipeDto> Handle(GetProductRecipeQuery request, CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Where(x => x.Id == request.ProductId)
            .Select(x => new { x.Id, x.Name, x.IsAvailable, x.TenantId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("Product", request.ProductId);

        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            product.TenantId,
            cancellationToken);
        if (!TenantCapabilityRules.CanAccessInventory(tenantCapabilities))
            throw new EntityNotFoundException("ProductRecipe", request.ProductId);

        var lines = await (
            from req in _context.ProductSupplyRequirements.AsNoTracking()
            join supply in _context.SupplyItems.AsNoTracking() on req.SupplyItemId equals supply.Id
            join location in _context.InventoryLocations.AsNoTracking() on req.InventoryLocationId equals location.Id into locationJoin
            from location in locationJoin.DefaultIfEmpty()
            where req.ProductId == request.ProductId
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
