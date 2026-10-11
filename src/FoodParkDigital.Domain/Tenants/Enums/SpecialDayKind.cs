namespace FoodParkDigital.Domain.Tenants.Enums;

/// <summary>What a special day does to the hours of its date.</summary>
public enum SpecialDayKind
{
    /// <summary>The business does not open that day: Navidad, inventario, vacaciones.</summary>
    Closed = 0,
    /// <summary>The business opens that day with other hours: Nochebuena until 18:00.</summary>
    SpecialHours = 1
}
