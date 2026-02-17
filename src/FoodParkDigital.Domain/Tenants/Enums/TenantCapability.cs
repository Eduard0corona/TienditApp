namespace FoodParkDigital.Domain.Tenants.Enums;

[Flags]
public enum TenantCapability
{
    None = 0,
    Retail = 1,
    Service = 2,
    Food = 4,
    Inventory = 8,
    Appointments = 16,
    Kitchen = 32
}
