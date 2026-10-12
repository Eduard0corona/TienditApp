using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Tenants.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.DeliveryPlatforms;

/// <summary>
/// Catálogo › Precios en apps: the products with what each costs in the menu and in the app of a delivery platform, and
/// what each of their options adds in both, by category and name. Services are left out: an order of a platform takes
/// products only. Whoever changes the menu's prices changes these.
/// </summary>
[RequiresPermission(TenantPermission.ManageCatalog)]
public record GetPlatformPriceListQuery(OrderChannel Channel, int PageNumber = 1, int PageSize = 100)
    : IRequest<PaginatedList<PlatformPriceItemDto>>;

public class GetPlatformPriceListQueryValidator : AbstractValidator<GetPlatformPriceListQuery>
{
    public GetPlatformPriceListQueryValidator()
    {
        RuleFor(x => x.Channel)
            .Must(OrderChannelRules.IsDeliveryPlatform)
            .WithMessage(DeliveryPlatformSetting.NotADeliveryPlatformMessage);
    }
}

public class GetPlatformPriceListQueryHandler : IRequestHandler<GetPlatformPriceListQuery, PaginatedList<PlatformPriceItemDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetPlatformPriceListQueryHandler(IApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PaginatedList<PlatformPriceItemDto>> Handle(GetPlatformPriceListQuery request, CancellationToken cancellationToken)
    {
        var capabilities = await TenantCapabilityRules.ResolveCapabilitiesAsync(_context, _tenantService.TenantId, cancellationToken);
        if (!TenantCapabilityRules.CanManageProductUnit(capabilities, UnitType.Unit))
            return new PaginatedList<PlatformPriceItemDto>([], 0, request.PageNumber, request.PageSize);

        var products = _context.Products
            .AsNoTracking()
            .Where(p => p.Unit != UnitType.Hour && p.Unit != UnitType.Session && p.Unit != UnitType.Service)
            .OrderBy(p => p.Category.SortOrder)
            .ThenBy(p => p.Category.Name)
            .ThenBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Select(p => new PlatformPriceItemDto
            {
                ProductId = p.Id,
                Name = p.Name,
                CategoryId = p.CategoryId,
                CategoryName = p.Category.Name,
                Unit = p.Unit,
                IsAvailable = p.IsAvailable,
                MenuPrice = p.PriceAmount,
                OptionGroups = p.OptionGroups
                    .OrderBy(g => g.SortOrder)
                    .Select(g => new PlatformPriceOptionGroupDto
                    {
                        Id = g.Id,
                        Name = g.Name,
                        Options = g.Options
                            .OrderBy(o => o.SortOrder)
                            .Select(o => new PlatformPriceOptionDto
                            {
                                Id = o.Id,
                                Name = o.Name,
                                IsAvailable = o.IsAvailable,
                                MenuPriceDelta = o.PriceDelta
                            })
                            .ToList()
                    })
                    .ToList()
            });

        var page = await PaginatedList<PlatformPriceItemDto>.CreateAsync(products, request.PageNumber, request.PageSize, cancellationToken);
        var prices = await ProductPlatformPriceReader.ReadAsync(
            _context,
            request.Channel,
            page.Items.Select(item => item.ProductId).ToList(),
            page.Items.SelectMany(item => item.OptionGroups).SelectMany(group => group.Options).Select(option => option.Id).ToList(),
            cancellationToken);
        foreach (var item in page.Items)
        {
            item.PlatformPrice = prices.Products.TryGetValue(item.ProductId, out var price) ? price : null;
            foreach (var option in item.OptionGroups.SelectMany(group => group.Options))
                option.PlatformPriceDelta = prices.Options.TryGetValue(option.Id, out var delta) ? delta : null;
        }

        return page;
    }
}

/// <summary>The prices the business set in the app of a delivery platform, read for a list of products.</summary>
public static class ProductPlatformPriceReader
{
    public sealed record Prices(IReadOnlyDictionary<Guid, decimal> Products, IReadOnlyDictionary<Guid, decimal> Options);

    /// <summary>The platform's prices of the products and options given (the business's only: the tenant filter).</summary>
    public static async Task<Prices> ReadAsync(
        IApplicationDbContext context,
        OrderChannel channel,
        IReadOnlyCollection<Guid> productIds,
        IReadOnlyCollection<Guid> optionIds,
        CancellationToken cancellationToken)
    {
        var products = productIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await context.ProductPlatformPrices
                .AsNoTracking()
                .Where(price => price.Channel == channel && productIds.Contains(price.ProductId))
                .ToDictionaryAsync(price => price.ProductId, price => price.Price, cancellationToken);
        var options = optionIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await context.ProductOptionPlatformPrices
                .AsNoTracking()
                .Where(price => price.Channel == channel && optionIds.Contains(price.OptionId))
                .ToDictionaryAsync(price => price.OptionId, price => price.PriceDelta, cancellationToken);
        return new Prices(products, options);
    }

    /// <summary>
    /// Prices a list of the catalog for an order of a delivery platform: each product and option takes the price the
    /// business set in the platform's app, and keeps the menu's where it set none. The crossed-out price is an offer of the
    /// menu: it goes away where the app's price applies.
    /// </summary>
    public static async Task ApplyAsync(
        IApplicationDbContext context,
        IReadOnlyCollection<ProductDto> products,
        OrderChannel channel,
        CancellationToken cancellationToken)
    {
        if (products.Count == 0)
            return;

        var options = products.SelectMany(product => product.OptionGroups).SelectMany(group => group.Options).ToList();
        var prices = await ReadAsync(
            context, channel, products.Select(product => product.Id).ToList(), options.Select(option => option.Id).ToList(), cancellationToken);
        foreach (var product in products)
        {
            if (!prices.Products.TryGetValue(product.Id, out var price))
                continue;
            product.PriceAmount = price;
            product.CompareAtPrice = null;
            product.HasDiscount = false;
        }
        foreach (var option in options)
        {
            if (prices.Options.TryGetValue(option.Id, out var delta))
                option.PriceDelta = delta;
        }
    }
}
