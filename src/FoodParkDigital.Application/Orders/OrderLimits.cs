using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Catalog.Entities;

namespace FoodParkDigital.Application.Orders;

/// <summary>
/// Upper bounds for a single order. Trackable services create one piece per unit, so unbounded
/// quantities could allocate millions of objects in a request shared by every tenant.
/// </summary>
public static class OrderLimits
{
    public const int MaxItemsPerOrder = 200;
    public const int MaxQuantityPerLine = 10_000;
    public const int MaxServicePiecesPerOrder = 500;

    /// <summary>
    /// Adds the line quantity to the running total of the product and returns an error when the
    /// product's MaxQuantityPerOrder is exceeded.
    /// </summary>
    public static string? TrackProductQuantity(Dictionary<Guid, int> quantitiesByProduct, Product product, int quantity)
    {
        quantitiesByProduct.TryGetValue(product.Id, out var current);
        var total = current + quantity;
        quantitiesByProduct[product.Id] = total;

        return product.MaxQuantityPerOrder is > 0 and var max && total > max
            ? $"El producto '{product.Name}' permite maximo {max} unidades por orden."
            : null;
    }

    /// <summary>
    /// Validates the service pieces an order would create: their number and that manual pieces only
    /// reference services that are part of the order.
    /// </summary>
    public static string? ValidateServicePieces(
        IReadOnlyCollection<ServiceOrderPieceInput> manualPieces,
        IEnumerable<(Product Product, CreateOrderItemDto Item)> trackableServiceLines,
        IReadOnlySet<Guid> orderProductIds)
    {
        var piecesToCreate = manualPieces.Count > 0
            ? manualPieces.Count
            : trackableServiceLines.Sum(line => line.Item.Quantity);

        if (piecesToCreate > MaxServicePiecesPerOrder)
            return $"Una orden puede generar como maximo {MaxServicePiecesPerOrder} piezas de servicio.";

        var foreignPiece = manualPieces.FirstOrDefault(piece =>
            piece.ServiceProductId.HasValue && !orderProductIds.Contains(piece.ServiceProductId.Value));

        return foreignPiece is null
            ? null
            : $"La pieza '{foreignPiece.ItemName}' referencia un servicio que no forma parte de la orden.";
    }
}
