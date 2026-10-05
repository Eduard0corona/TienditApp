using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Purchasing.DTOs;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Purchasing.Queries;

[RequiresPermission(TenantPermission.ManageInventory)]
[RequiresCapability(TenantCapability.Inventory)]
public record GetSupplierByIdQuery(Guid Id) : IRequest<SupplierDto>;

public class GetSupplierByIdQueryHandler : IRequestHandler<GetSupplierByIdQuery, SupplierDto>
{
    private readonly IApplicationDbContext _context;

    public GetSupplierByIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<SupplierDto> Handle(GetSupplierByIdQuery request, CancellationToken cancellationToken)
    {
        return await _context.Suppliers
            .AsNoTracking()
            .Where(x => x.Id == request.Id)
            .Select(x => new SupplierDto
            {
                Id = x.Id,
                Name = x.Name,
                ContactName = x.ContactName,
                Phone = x.Phone,
                Email = x.Email,
                TaxId = x.TaxId,
                Address = x.Address,
                Notes = x.Notes,
                IsActive = x.IsActive
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("Supplier", request.Id);
    }
}
