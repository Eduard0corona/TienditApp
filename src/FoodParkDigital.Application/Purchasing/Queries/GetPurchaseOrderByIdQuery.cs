using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Purchasing.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Purchasing.Queries;

[RequiresPermission(TenantPermission.ManageInventory)]
public record GetPurchaseOrderByIdQuery(Guid Id) : IRequest<PurchaseOrderDto>;

public class GetPurchaseOrderByIdQueryHandler : IRequestHandler<GetPurchaseOrderByIdQuery, PurchaseOrderDto>
{
    private readonly IApplicationDbContext _context;

    public GetPurchaseOrderByIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PurchaseOrderDto> Handle(GetPurchaseOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var header = await (
            from po in _context.PurchaseOrders.AsNoTracking()
            join supplier in _context.Suppliers.AsNoTracking() on po.SupplierId equals supplier.Id
            where po.Id == request.Id
            select new
            {
                po.Id,
                po.SupplierId,
                SupplierName = supplier.Name,
                po.OrderNumber,
                po.Status,
                po.OrderedAt,
                po.ExpectedDeliveryAt,
                po.ReceivedAt,
                po.ReferenceNumber,
                po.Notes,
                po.SubTotal,
                po.Tax,
                po.Total
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("PurchaseOrder", request.Id);

        var lines = await (
            from line in _context.PurchaseOrderLines.AsNoTracking()
            join supply in _context.SupplyItems.AsNoTracking() on line.SupplyItemId equals supply.Id
            join location in _context.InventoryLocations.AsNoTracking() on line.InventoryLocationId equals location.Id
            where line.PurchaseOrderId == request.Id
            orderby line.CreatedAt
            select new PurchaseOrderLineDto
            {
                Id = line.Id,
                SupplyItemId = line.SupplyItemId,
                SupplyItemName = supply.Name,
                InventoryLocationId = line.InventoryLocationId,
                InventoryLocationName = location.Name,
                Quantity = line.Quantity,
                ReceivedQuantity = line.ReceivedQuantity,
                PendingQuantity = line.Quantity - line.ReceivedQuantity,
                UnitCost = line.UnitCost,
                TaxPercent = line.TaxPercent,
                SubTotal = line.SubTotal,
                TaxAmount = line.TaxAmount,
                Total = line.Total,
                Notes = line.Notes
            })
            .ToListAsync(cancellationToken);

        return new PurchaseOrderDto
        {
            Id = header.Id,
            SupplierId = header.SupplierId,
            SupplierName = header.SupplierName,
            OrderNumber = header.OrderNumber,
            Status = header.Status,
            OrderedAt = header.OrderedAt,
            ExpectedDeliveryAt = header.ExpectedDeliveryAt,
            ReceivedAt = header.ReceivedAt,
            ReferenceNumber = header.ReferenceNumber,
            Notes = header.Notes,
            SubTotal = header.SubTotal,
            Tax = header.Tax,
            Total = header.Total,
            Lines = lines
        };
    }
}
