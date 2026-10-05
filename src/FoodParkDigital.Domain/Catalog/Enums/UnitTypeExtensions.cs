namespace FoodParkDigital.Domain.Catalog.Enums;

public static class UnitTypeExtensions
{
    /// <summary>Hours, sessions and services: sold as time or work, never kept in stock.</summary>
    public static bool IsService(this UnitType unit) => unit is UnitType.Hour or UnitType.Session or UnitType.Service;
}
