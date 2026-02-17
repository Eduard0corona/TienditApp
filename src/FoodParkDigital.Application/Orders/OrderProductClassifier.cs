using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Orders.Enums;

namespace FoodParkDigital.Application.Orders;

public static class OrderProductClassifier
{
    public static bool IsServiceUnit(UnitType? unit) =>
        unit is UnitType.Hour or UnitType.Session or UnitType.Service;

    public static bool MatchesOrderType(OrderType orderType, UnitType? unit) =>
        orderType switch
        {
            OrderType.Service => IsServiceUnit(unit),
            OrderType.Retail => !IsServiceUnit(unit),
            _ => false
        };
}
