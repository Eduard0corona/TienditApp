using FoodParkDigital.Application.Catalog.DTOs;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Inventory.DTOs;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Inventory;

/// <summary>
/// The stock of products that control it, read from the inventory ledger (their item's stock in every location). The
/// catalog, Vender and the product screen show it; a product without control has none.
/// </summary>
public static class ProductStockReader
{
    public static async Task ApplyAsync(IApplicationDbContext context, IReadOnlyCollection<ProductDto> products, CancellationToken cancellationToken)
    {
        if (products.Count == 0)
            return;

        var productIds = products.Select(p => p.Id).ToList();
        var items = await context.SupplyItems
            .AsNoTracking()
            .Where(s => s.ProductId != null && productIds.Contains(s.ProductId.Value) && s.IsActive)
            .Select(s => new
            {
                ProductId = s.ProductId!.Value,
                s.Id,
                s.ReorderPoint,
                OnHand = context.InventoryStocks
                    .Where(stock => stock.SupplyItemId == s.Id)
                    .Select(stock => (decimal?)stock.OnHandQuantity)
                    .Sum() ?? 0
            })
            .ToDictionaryAsync(s => s.ProductId, cancellationToken);

        foreach (var product in products)
        {
            if (items.TryGetValue(product.Id, out var item))
            {
                product.TracksStock = true;
                product.StockItemId = item.Id;
                product.OnHand = item.OnHand;
                product.ReorderPoint = item.ReorderPoint;
                product.IsLowStock = item.OnHand <= item.ReorderPoint;
            }
            else
            {
                product.TracksStock = false;
                product.StockItemId = null;
                product.OnHand = null;
                product.ReorderPoint = null;
                product.IsLowStock = false;
            }
        }
    }

    public static Task ApplyAsync(IApplicationDbContext context, ProductDto product, CancellationToken cancellationToken)
        => ApplyAsync(context, [product], cancellationToken);

    /// <summary>The product's stock by location, or null when the product does not exist.</summary>
    public static async Task<ProductStockDto?> GetAsync(IApplicationDbContext context, Guid productId, CancellationToken cancellationToken)
    {
        var product = await context.Products
            .AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.Id, p.StockQuantity, p.LowStockThreshold })
            .FirstOrDefaultAsync(cancellationToken);
        if (product is null)
            return null;

        var dto = new ProductStockDto
        {
            ProductId = product.Id,
            CapturedStockQuantity = product.StockQuantity,
            CapturedLowStockThreshold = product.LowStockThreshold
        };

        var item = await context.SupplyItems
            .AsNoTracking()
            .Where(s => s.ProductId == productId && s.IsActive)
            .Select(s => new { s.Id, s.ReorderPoint })
            .FirstOrDefaultAsync(cancellationToken);
        if (item is null)
            return dto;

        dto.TracksStock = true;
        dto.StockItemId = item.Id;
        dto.ReorderPoint = item.ReorderPoint;
        dto.Locations = await (
                from stock in context.InventoryStocks.AsNoTracking()
                join location in context.InventoryLocations.AsNoTracking() on stock.InventoryLocationId equals location.Id
                where stock.SupplyItemId == item.Id
                orderby location.Name
                select new ProductStockLocationDto
                {
                    InventoryLocationId = location.Id,
                    LocationName = location.Name,
                    OnHand = stock.OnHandQuantity
                })
            .ToListAsync(cancellationToken);
        dto.OnHand = dto.Locations.Sum(l => l.OnHand);
        dto.IsLowStock = dto.OnHand <= dto.ReorderPoint;
        return dto;
    }
}
