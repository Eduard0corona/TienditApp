using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Purchasing.DTOs;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;

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

    public Task<PurchaseOrderDto> Handle(GetPurchaseOrderByIdQuery request, CancellationToken cancellationToken)
        => PurchaseOrderReader.LoadAsync(_context, request.Id, cancellationToken);
}
