using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Inventory.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;

namespace FoodParkDigital.Application.Inventory.Queries;

/// <summary>The stock of a product by location, and what was captured by hand before it was controlled.</summary>
[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record GetProductStockQuery(Guid ProductId) : IRequest<ProductStockDto>;

public class GetProductStockQueryHandler : IRequestHandler<GetProductStockQuery, ProductStockDto>
{
    private readonly IApplicationDbContext _context;

    public GetProductStockQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ProductStockDto> Handle(GetProductStockQuery request, CancellationToken cancellationToken)
        => await ProductStockReader.GetAsync(_context, request.ProductId, cancellationToken)
           ?? throw new EntityNotFoundException("Product", request.ProductId);
}
