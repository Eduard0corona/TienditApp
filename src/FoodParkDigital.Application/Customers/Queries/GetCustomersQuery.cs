using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Customers.DTOs;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Customers.Queries;

[RequiresPermission(TenantPermission.ManageCustomers)]
public record GetCustomersQuery : IRequest<PaginatedList<CustomerDto>>
{
    public string? Search { get; init; }
    public bool? IsActive { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetCustomersQueryHandler : IRequestHandler<GetCustomersQuery, PaginatedList<CustomerDto>>
{
    private readonly IApplicationDbContext _context;

    public GetCustomersQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<CustomerDto>> Handle(GetCustomersQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Customers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(search) ||
                (c.Phone != null && c.Phone.ToLower().Contains(search)) ||
                (c.Email != null && c.Email.ToLower().Contains(search)));
        }

        if (request.IsActive.HasValue)
            query = query.Where(c => c.IsActive == request.IsActive.Value);

        query = query
            .OrderByDescending(c => c.IsPublicGeneral)
            .ThenBy(c => c.Name);

        // The list carries the notes and the fiscal data but not the addresses: the detail loads them.
        var projected = query.Select(c => new CustomerDto
        {
            Id = c.Id,
            Name = c.Name,
            Phone = c.Phone,
            Email = c.Email,
            IsPublicGeneral = c.IsPublicGeneral,
            IsActive = c.IsActive,
            Notes = c.Notes,
            CreditLimit = c.CreditLimit,
            CreditTermDays = c.CreditTermDays,
            FiscalData = c.FiscalData == null ? null : new CfdiReceiverDto
            {
                Rfc = c.FiscalData.Rfc,
                Name = c.FiscalData.Name,
                PostalCode = c.FiscalData.PostalCode,
                FiscalRegime = c.FiscalData.FiscalRegime,
                CfdiUse = c.FiscalData.CfdiUse
            },
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        });
        return await PaginatedList<CustomerDto>.CreateAsync(
            projected,
            request.PageNumber,
            request.PageSize,
            cancellationToken);
    }
}
