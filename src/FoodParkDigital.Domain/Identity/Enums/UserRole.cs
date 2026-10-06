namespace FoodParkDigital.Domain.Identity.Enums;

/// <summary>
/// The role of an account. Zero is no role, so a role left out (a default value, an omitted field) grants nothing;
/// the platform admin is a value nobody gets by default.
/// </summary>
public enum UserRole
{
    None = 0,
    TenantAdmin = 1,
    Staff = 2,
    Kitchen = 3,
    Customer = 4,
    SuperAdmin = 9
}
