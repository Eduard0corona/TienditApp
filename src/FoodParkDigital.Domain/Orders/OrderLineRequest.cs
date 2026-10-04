using FoodParkDigital.Domain.Common.ValueObjects;

namespace FoodParkDigital.Domain.Orders;

/// <summary>A line to add to an order, already priced and checked against the catalog by the caller.</summary>
public sealed record OrderLineRequest(
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    string? Notes,
    VatRate VatRate,
    OrderLineFiscalData Fiscal,
    IReadOnlyList<OrderLineOption> Options);
