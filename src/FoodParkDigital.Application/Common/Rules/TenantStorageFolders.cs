namespace FoodParkDigital.Application.Common.Rules;

// Tenant-owned files live under "{area}/{tenantId}". Uploads and deletions use the same folder so the
// storage provider can refuse URLs that point to another tenant or area.
public static class TenantStorageFolders
{
    public static string ProductImages(Guid tenantId) => $"products/{tenantId}";

    public static string Logos(Guid tenantId) => $"logos/{tenantId}";

    public static string Avatars(Guid tenantId) => $"avatars/{tenantId}";
}
