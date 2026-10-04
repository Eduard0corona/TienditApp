using AutoMapper;
using AutoMapper.QueryableExtensions;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Domain.Orders.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders.Queries;

public record GetOrdersByBusinessQuery : IRequest<PaginatedList<OrderDto>>
{
    public OrderType? OrderTypeFilter { get; init; }
    public OrderStatus? StatusFilter { get; init; }
    public DateTimeOffset? FromDate { get; init; }
    public DateTimeOffset? ToDate { get; init; }
    /// <summary>Folio, customer name or phone digits.</summary>
    public string? Search { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetOrdersByBusinessQueryValidator : AbstractValidator<GetOrdersByBusinessQuery>
{
    public GetOrdersByBusinessQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public class GetOrdersByBusinessQueryHandler : IRequestHandler<GetOrdersByBusinessQuery, PaginatedList<OrderDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IMapper _mapper;

    public GetOrdersByBusinessQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService, IMapper mapper)
    {
        _context = context;
        _tenantService = tenantService;
        _mapper = mapper;
    }

    public async Task<PaginatedList<OrderDto>> Handle(GetOrdersByBusinessQuery request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);

        var canReadRetailOrders = TenantCapabilityRules.CanCreateOrderType(tenantCapabilities, OrderType.Retail);
        var canReadServiceOrders = TenantCapabilityRules.CanCreateOrderType(tenantCapabilities, OrderType.Service);

        if (!canReadRetailOrders && !canReadServiceOrders)
            return new PaginatedList<OrderDto>([], 0, request.PageNumber, request.PageSize);

        var query = _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.ServiceDetail)
            .Include(o => o.ServicePieces)
            .Include(o => o.Payments)
            .AsQueryable();

        if (request.StatusFilter.HasValue)
            query = query.Where(o => o.Status == request.StatusFilter.Value);

        if (request.OrderTypeFilter.HasValue)
        {
            if (!TenantCapabilityRules.CanCreateOrderType(tenantCapabilities, request.OrderTypeFilter.Value))
                return new PaginatedList<OrderDto>([], 0, request.PageNumber, request.PageSize);

            query = query.Where(o => o.OrderType == request.OrderTypeFilter.Value);
        }
        else
        {
            if (canReadRetailOrders && !canReadServiceOrders)
                query = query.Where(o => o.OrderType == OrderType.Retail);
            else if (!canReadRetailOrders && canReadServiceOrders)
                query = query.Where(o => o.OrderType == OrderType.Service);
        }

        if (request.FromDate.HasValue)
            query = query.Where(o => o.CreatedAt >= request.FromDate.Value);

        if (request.ToDate.HasValue)
            query = query.Where(o => o.CreatedAt <= request.ToDate.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Lower-cased on both sides so the match does not depend on the database collation.
            var term = request.Search.Trim().ToLowerInvariant();
            // Phones are stored normalized (+5215512345678): compare the digits that were typed.
            var digits = new string(term.Where(char.IsDigit).ToArray());
            var searchPhone = digits.Length >= 4;
            query = query.Where(o =>
                o.OrderNumber.ToLower().Contains(term)
                || o.CustomerName.ToLower().Contains(term)
                || (searchPhone && o.CustomerPhone != null && o.CustomerPhone.Contains(digits)));
        }

        query = query.OrderByDescending(o => o.CreatedAt);

        var projectedQuery = query.ProjectTo<OrderDto>(_mapper.ConfigurationProvider);
        var result = await PaginatedList<OrderDto>.CreateAsync(
            projectedQuery, request.PageNumber, request.PageSize, cancellationToken);
        result.Items.ApplyStateMetadata();
        return result;
    }
}
