using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;

namespace FoodParkDigital.Application.Orders;

/// <summary>A requested line checked against its product and priced with its options.</summary>
public sealed record BuiltOrderLine(Product Product, OrderLineRequest Line, bool IsTrackableService);

/// <summary>
/// Turns requested lines into priced lines checked against the catalog: available now (or just available, for an order
/// of a delivery platform), options valid (each group within its minimum and maximum), quantity limits per product, IVA
/// and invoicing codes. Shared by counter sales, open tabs and online orders, so a line is priced the same way everywhere.
/// </summary>
public sealed class OrderLineBuilder
{
    private readonly Dictionary<Guid, decimal> _quantitiesByProduct = new();
    private readonly TimeSpan _localTimeOfDay;
    private readonly decimal _defaultTaxRate;
    private readonly bool _enforceProductHours;

    /// <param name="enforceProductHours">
    /// False for an order a delivery platform's app already took, whatever the hours of each product here: the product
    /// must still be available (a product the business turned off is refused).
    /// </param>
    public OrderLineBuilder(
        TimeSpan localTimeOfDay,
        decimal defaultTaxRate,
        IEnumerable<OrderItem>? existingItems = null,
        bool enforceProductHours = true)
    {
        _localTimeOfDay = localTimeOfDay;
        _defaultTaxRate = defaultTaxRate;
        _enforceProductHours = enforceProductHours;
        // Rounds of an open tab add to what the tab already has (MaxQuantityPerOrder).
        foreach (var item in existingItems ?? [])
            _quantitiesByProduct[item.ProductId] = _quantitiesByProduct.GetValueOrDefault(item.ProductId) + item.Quantity;
    }

    /// <summary>Products of the lines built so far with their total quantity.</summary>
    public IReadOnlyDictionary<Guid, decimal> QuantitiesByProduct => _quantitiesByProduct;

    /// <summary>
    /// Checks a requested line against its product and prices it. Catalog rules (availability, options) throw
    /// <see cref="DomainException"/>; order limits come back as a failure.
    /// </summary>
    public Result<BuiltOrderLine> Build(Product product, CreateOrderItemDto item)
    {
        if (_enforceProductHours && !product.IsAvailableAt(_localTimeOfDay))
            throw new DomainException($"El producto '{product.Name}' no está disponible en este horario.");
        if (!_enforceProductHours && !product.IsAvailable)
            throw new DomainException($"El producto '{product.Name}' no está disponible.");

        var quantityError = OrderLimits.TrackProductQuantity(_quantitiesByProduct, product, item.Quantity);
        if (quantityError is not null)
            return Result.Failure<BuiltOrderLine>(quantityError);

        var isTrackableService = OrderProductClassifier.IsServiceUnit(product.Unit) && product.IsTrackable;
        if (isTrackableService && decimal.Truncate(item.Quantity) != item.Quantity)
            return Result.Failure<BuiltOrderLine>($"'{product.Name}' crea una pieza por unidad: la cantidad debe ser entera.");

        var options = product.ResolveOptions(item.OptionIds ?? []);
        var line = new OrderLineRequest(
            product.Id,
            product.Name,
            item.Quantity,
            product.PriceWith(options),
            string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim(),
            product.GetVatRate(_defaultTaxRate),
            OrderLineFiscalData.From(product),
            options);
        return Result.Success(new BuiltOrderLine(product, line, isTrackableService));
    }
}
