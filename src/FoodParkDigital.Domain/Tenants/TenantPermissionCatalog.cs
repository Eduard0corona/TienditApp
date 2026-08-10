using FoodParkDigital.Domain.Identity.Enums;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tenants;

public static class TenantPermissionCatalog
{
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
        var normalized = NormalizeCodes(
            string.IsNullOrWhiteSpace(explicitCodes)
                ? []
                : explicitCodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (normalized.Count > 0)
            return normalized;

        return GetDefaultCodes(userRole, tenantRole);
    }

    public static IReadOnlyCollection<string> GetDefaultCodes(UserRole userRole, TenantUserRole tenantRole)
    {
        if (userRole is UserRole.SuperAdmin or UserRole.TenantAdmin
            || tenantRole is TenantUserRole.Owner or TenantUserRole.Admin)
        {
            return GetAllCodes();
        }

        if (userRole == UserRole.Kitchen)
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
}
