namespace FoodParkDigital.Domain.Orders.Enums;

/// <summary>The amount of the order a delivery platform takes its commission from.</summary>
public enum PlatformCommissionBase
{
    /// <summary>The order total, IVA included.</summary>
    TotalWithTax = 0,
    /// <summary>The subtotal, without IVA.</summary>
    SubTotalWithoutTax = 1
}
