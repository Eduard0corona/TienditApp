using FoodParkDigital.Application.Common.Interfaces;

namespace FoodParkDigital.Application.Tenants;

public static class TenantManagementRules
{
    public const string NotAllowedMessage = "Solo el administrador del negocio puede cambiar su configuración.";

    /// <summary>Owners and admins of the business, or the platform, change its configuration.</summary>
    public static bool CanManageTenant(ICurrentUserService currentUser) =>
        currentUser.Role is "TENANT_ADMIN" or "SUPER_ADMIN"
        || string.Equals(currentUser.TenantRole, "OWNER", StringComparison.OrdinalIgnoreCase)
        || string.Equals(currentUser.TenantRole, "ADMIN", StringComparison.OrdinalIgnoreCase);
}
