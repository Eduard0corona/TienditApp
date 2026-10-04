using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Purchasing.DTOs;
using FoodParkDigital.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Purchasing;

/// <summary>The detail of a purchase order as the screens show it: supplier, lines with supply and location names.</summary>
internal static class PurchaseOrderReader
{
    public static async Task<PurchaseOrderDto> LoadAsync(IApplicationDbContext context, Guid purchaseOrderId, CancellationToken cancellationToken)
    {
        var header = await (
            from po in context.PurchaseOrders.AsNoTracking()
            join supplier in context.Suppliers.AsNoTracking() on po.SupplierId equals supplier.Id
            where po.Id == purchaseOrderId
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
            ?? throw new EntityNotFoundException("PurchaseOrder", purchaseOrderId);

        var lines = await (
            from line in context.PurchaseOrderLines.AsNoTracking()
            join supply in context.SupplyItems.AsNoTracking() on line.SupplyItemId equals supply.Id
            join location in context.InventoryLocations.AsNoTracking() on line.InventoryLocationId equals location.Id
            where line.PurchaseOrderId == purchaseOrderId
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
