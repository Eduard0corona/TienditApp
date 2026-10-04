using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Orders;

/// <summary>Loads the products of requested lines, with their options, in one query.</summary>
public static class OrderCatalog
{
    /// <param name="products">The tenant's products (already filtered by tenant, or narrowed by the caller).</param>
    public static async Task<IReadOnlyDictionary<Guid, Product>> LoadProductsAsync(
        IQueryable<Product> products,
        IEnumerable<CreateOrderItemDto> items,
        CancellationToken cancellationToken)
    {
        var ids = items.Select(item => item.ProductId).Distinct().ToList();
        var loaded = await products
            .Include(product => product.OptionGroups)
            .ThenInclude(group => group.Options)
            .Where(product => ids.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        var missing = ids.FirstOrDefault(id => !loaded.ContainsKey(id));
        return missing == Guid.Empty ? loaded : throw new EntityNotFoundException("Product", missing);
    }
}
