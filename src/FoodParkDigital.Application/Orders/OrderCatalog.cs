using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders;

/// <summary>Loads the products of requested lines, with their options, in one query.</summary>
public static class OrderCatalog
{
    /// <param name="products">The tenant's products (already filtered by tenant, or narrowed by the caller).</param>
    /// <param name="channel">
    /// Where the order comes from: for a delivery platform, the prices the business set in its app come with the products
    /// and their options (only that platform's).
    /// </param>
    public static async Task<IReadOnlyDictionary<Guid, Product>> LoadProductsAsync(
        IQueryable<Product> products,
        IEnumerable<CreateOrderItemDto> items,
        CancellationToken cancellationToken,
        OrderChannel channel = OrderChannel.Counter)
    {
        var ids = items.Select(item => item.ProductId).Distinct().ToList();
        IQueryable<Product> query = products
            .Include(product => product.OptionGroups)
            .ThenInclude(group => group.Options);
        if (OrderChannelRules.IsDeliveryPlatform(channel))
        {
            query = query
                .Include(product => product.PlatformPrices.Where(price => price.Channel == channel))
                .Include(product => product.OptionGroups)
                .ThenInclude(group => group.Options)
                .ThenInclude(option => option.PlatformPrices.Where(price => price.Channel == channel));
        }

        var loaded = await query
            .Where(product => ids.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        var missing = ids.FirstOrDefault(id => !loaded.ContainsKey(id));
        return missing == Guid.Empty ? loaded : throw new EntityNotFoundException("Product", missing);
    }
}
