using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Purchasing.DTOs;
using FoodParkDigital.Domain.Purchasing.Enums;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Purchasing.Queries;

[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record GetPurchaseOrdersQuery : IRequest<PaginatedList<PurchaseOrderDto>>
{
    public Guid? SupplierId { get; init; }
    public PurchaseOrderStatus? Status { get; init; }
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetPurchaseOrdersQueryHandler : IRequestHandler<GetPurchaseOrdersQuery, PaginatedList<PurchaseOrderDto>>
{
    private readonly IApplicationDbContext _context;

    public GetPurchaseOrdersQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<PurchaseOrderDto>> Handle(GetPurchaseOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = from po in _context.PurchaseOrders.AsNoTracking()
                    join supplier in _context.Suppliers.AsNoTracking() on po.SupplierId equals supplier.Id
                    select new PurchaseOrderDto
                    {
                        Id = po.Id,
                        SupplierId = po.SupplierId,
                        SupplierName = supplier.Name,
                        OrderNumber = po.OrderNumber,
                        Status = po.Status,
                        OrderedAt = po.OrderedAt,
                        ExpectedDeliveryAt = po.ExpectedDeliveryAt,
                        ReceivedAt = po.ReceivedAt,
                        ReferenceNumber = po.ReferenceNumber,
                        Notes = po.Notes,
                        SubTotal = po.SubTotal,
                        Tax = po.Tax,
                        Total = po.Total
                    };

        if (request.SupplierId.HasValue)
            query = query.Where(x => x.SupplierId == request.SupplierId.Value);
        if (request.Status.HasValue)
            query = query.Where(x => x.Status == request.Status.Value);
        if (request.FromDate.HasValue)
            query = query.Where(x => x.OrderedAt >= request.FromDate.Value);
        if (request.ToDate.HasValue)
            query = query.Where(x => x.OrderedAt <= request.ToDate.Value);

        query = query.OrderByDescending(x => x.OrderedAt);

        return await PaginatedList<PurchaseOrderDto>.CreateAsync(
            query,
            request.PageNumber,
            request.PageSize,
            cancellationToken);
    }
}
