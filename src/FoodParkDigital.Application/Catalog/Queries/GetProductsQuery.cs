using AutoMapper;
using AutoMapper.QueryableExtensions;
using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Domain.Catalog.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Catalog.Queries;

public enum ProductCatalogItemType
{
    Product = 0,
    Service = 1
}

public record GetProductsQuery : IRequest<PaginatedList<ProductDto>>
{
    public Guid? CategoryId { get; init; }
    public bool? IsAvailable { get; init; }
    public bool OnlyAvailableNow { get; init; }
    public ProductCatalogItemType? ItemType { get; init; }
    /// <summary>Name, SKU or barcode (a scanned code matches its product exactly).</summary>
    public string? Search { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class GetProductsQueryValidator : AbstractValidator<GetProductsQuery>
{
    public GetProductsQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, PaginatedList<ProductDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IMapper _mapper;

    public GetProductsQueryHandler(
        IApplicationDbContext context,
        ICurrentTenantService tenantService,
        IDateTimeProvider dateTimeProvider,
        IMapper mapper)
    {
        _context = context;
        _tenantService = tenantService;
        _dateTimeProvider = dateTimeProvider;
        _mapper = mapper;
    }

    public async Task<PaginatedList<ProductDto>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        var tenantCapabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(
            _context,
            _tenantService.TenantId,
            cancellationToken);
        var canViewProducts = TenantCapabilityRules.CanManageProductUnit(tenantCapabilities, UnitType.Unit);
        var canViewServices = TenantCapabilityRules.CanManageProductUnit(tenantCapabilities, UnitType.Service);

        if (!canViewProducts && !canViewServices)
            return new PaginatedList<ProductDto>([], 0, request.PageNumber, request.PageSize);

        var query = _context.Products.AsNoTracking().AsQueryable();

        if (request.CategoryId.HasValue)
            query = query.Where(p => p.CategoryId == request.CategoryId.Value);

        if (request.IsAvailable.HasValue)
            query = query.Where(p => p.IsAvailable == request.IsAvailable.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Lower-cased on both sides so the match does not depend on the database collation.
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(p =>
                p.Name.ToLower().Contains(term)
                || (p.Sku != null && p.Sku.ToLower().Contains(term))
                || (p.Barcode != null && p.Barcode.ToLower() == term));
        }

        if (request.OnlyAvailableNow)
        {
            var timeZoneId = await _context.TenantSettings
                .AsNoTracking()
                .Where(x => x.TenantId == _tenantService.TenantId)
                .Select(x => x.TimeZone)
                .FirstOrDefaultAsync(cancellationToken);

            var tenantZone = TenantDateTimeResolver.Resolve(timeZoneId);
            var localTimeOfDay = TenantDateTimeResolver.GetLocalTimeOfDay(_dateTimeProvider.UtcNow, tenantZone);

            query = query.Where(p =>
                !p.AvailableFromTime.HasValue ||
                !p.AvailableToTime.HasValue ||
                (
                    p.AvailableFromTime <= p.AvailableToTime
                        ? localTimeOfDay >= p.AvailableFromTime && localTimeOfDay <= p.AvailableToTime
                        : localTimeOfDay >= p.AvailableFromTime || localTimeOfDay <= p.AvailableToTime
                ));
        }

        if (request.ItemType.HasValue)
        {
            if (request.ItemType.Value == ProductCatalogItemType.Service)
            {
                if (!canViewServices)
                    return new PaginatedList<ProductDto>([], 0, request.PageNumber, request.PageSize);

                query = query.Where(p =>
                    p.Unit == UnitType.Hour ||
                    p.Unit == UnitType.Session ||
                    p.Unit == UnitType.Service);
            }
            else
            {
                if (!canViewProducts)
                    return new PaginatedList<ProductDto>([], 0, request.PageNumber, request.PageSize);

                query = query.Where(p =>
                    p.Unit != UnitType.Hour &&
                    p.Unit != UnitType.Session &&
                    p.Unit != UnitType.Service);
            }
        }
        else if (canViewProducts && !canViewServices)
        {
            query = query.Where(p =>
                p.Unit != UnitType.Hour &&
                p.Unit != UnitType.Session &&
                p.Unit != UnitType.Service);
        }
        else if (!canViewProducts && canViewServices)
        {
            query = query.Where(p =>
                p.Unit == UnitType.Hour ||
                p.Unit == UnitType.Session ||
                p.Unit == UnitType.Service);
        }

        var projectedQuery = query
            .OrderBy(p => p.Name)
            .ProjectTo<ProductDto>(_mapper.ConfigurationProvider);

        return await PaginatedList<ProductDto>.CreateAsync(
            projectedQuery, request.PageNumber, request.PageSize, cancellationToken);
    }
}
