namespace FoodParkDigital.Domain.Orders;

/// <summary>
/// An option chosen for a sold line, copied from the catalog when it is sold so a later change to the product
/// never alters the order.
/// </summary>
public sealed record OrderLineOption(Guid OptionId, string GroupName, string OptionName, decimal PriceDelta);
