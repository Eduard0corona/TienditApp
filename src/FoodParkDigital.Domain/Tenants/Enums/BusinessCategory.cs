namespace FoodParkDigital.Domain.Tenants.Enums;

/// <summary>
/// Where a business is listed in Explorar, the directory of TienditApp (ADR 0021). The owner picks one; it starts from
/// the giro (<see cref="BusinessCategoryDefaults"/>). It is not the type of business, which sets the modules, nor a
/// category of products. The client names each one and the database keeps the number.
/// </summary>
public enum BusinessCategory
{
    Food = 0,
    Groceries = 1,
    Health = 2,
    Apparel = 3,
    Electronics = 4,
    Hardware = 5,
    Stationery = 6,
    Beauty = 7,
    Automotive = 8,
    Services = 9,
    Other = 10,

    /// <summary>«Hogar y reparaciones»: the trades that go to the customer's home (ADR 0053).</summary>
    HomeRepairs = 11
}
