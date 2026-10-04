using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tenants;

public static class TenantPermissionCatalog
{
    /// <summary>
    /// Stored when a person was saved without any permission on purpose ("sin acceso"). A null value still means the
    /// defaults of their role.
    /// </summary>
    public const string NoAccessValue = "None";

    private static readonly HashSet<string> ValidCodes = Enum.GetNames<TenantPermission>()
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> GetAllCodes() =>
        Enum.GetNames<TenantPermission>();

    public static IReadOnlyCollection<string> NormalizeCodes(IEnumerable<string>? codes)
    {
        if (codes is null)
            return [];

        var normalized = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawCode in codes)
        {
            if (string.IsNullOrWhiteSpace(rawCode))
                continue;

            var code = rawCode.Trim();
            if (ValidCodes.Contains(code))
                normalized.Add(code);
        }

        return normalized.ToList();
    }

    public static IReadOnlyCollection<string> ResolveEffectiveCodes(
        string? explicitCodes,
        UserRole userRole,
        TenantUserRole tenantRole)
    {
        if (string.Equals(explicitCodes?.Trim(), NoAccessValue, StringComparison.OrdinalIgnoreCase))
            return [];

        var normalized = NormalizeCodes(
            string.IsNullOrWhiteSpace(explicitCodes)
                ? []
                : explicitCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (normalized.Count > 0)
            return normalized;

        return GetDefaultCodes(userRole, tenantRole);
    }

    /// <summary>
    /// The role a person has inside one business. The membership decides it: owners and admins of the business
    /// administer it; anyone else is staff there (kitchen staff if their account is a kitchen one), even if they
    /// own another business. Only the platform admin keeps their role everywhere.
    /// </summary>
    public static UserRole EffectiveRole(UserRole userRole, TenantUserRole tenantRole) => userRole switch
    {
        UserRole.SuperAdmin => UserRole.SuperAdmin,
        UserRole.Customer => UserRole.Customer,
        _ when tenantRole is TenantUserRole.Owner or TenantUserRole.Admin => UserRole.TenantAdmin,
        UserRole.Kitchen => UserRole.Kitchen,
        _ => UserRole.Staff
    };

    public static IReadOnlyCollection<string> GetDefaultCodes(UserRole userRole, TenantUserRole tenantRole)
    {
        var effectiveRole = EffectiveRole(userRole, tenantRole);
        if (effectiveRole is UserRole.SuperAdmin or UserRole.TenantAdmin)
            return GetAllCodes();

        if (effectiveRole == UserRole.Kitchen)
        {
            return
            [
                nameof(TenantPermission.ManageOrders),
                nameof(TenantPermission.ManageServiceBoard)
            ];
        }

        return
        [
            nameof(TenantPermission.ManageCatalog),
            nameof(TenantPermission.ManageOrders),
            nameof(TenantPermission.ChargeOrders),
            nameof(TenantPermission.ManageServiceBoard),
            nameof(TenantPermission.ManageInventory),
            nameof(TenantPermission.ManageCash),
            nameof(TenantPermission.ManageCustomers),
            nameof(TenantPermission.ViewReports),
            nameof(TenantPermission.ManageWhatsApp),
            nameof(TenantPermission.ManageAppointments)
        ];
    }

    public static string? ToStoredValue(IEnumerable<string>? codes)
    {
        var normalized = NormalizeCodes(codes);
        return normalized.Count == 0 ? null : string.Join(',', normalized);
    }

    /// <summary>What was chosen, where choosing nothing is "sin acceso" rather than the role's defaults.</summary>
    public static string ToExplicitStoredValue(IEnumerable<string> codes) => ToStoredValue(codes) ?? NoAccessValue;
}
