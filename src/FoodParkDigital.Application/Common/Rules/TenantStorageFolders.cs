namespace FoodParkDigital.Application.Common.Rules;

// Files live under "{area}/{owner}": the business for product images and logos, the account for avatars. Uploads and
// deletions use the same folder so the storage provider can refuse URLs that point to another owner or area.
public static class TenantStorageFolders
{
    public static string ProductImages(Guid tenantId) => $"products/{tenantId}";

    public static string Logos(Guid tenantId) => $"logos/{tenantId}";

    /// <summary>The avatar of an account: the person is the same in every business, and so is their folder.</summary>
    public static string UserAvatars(Guid userId) => $"avatars/users/{userId}";

    /// <summary>Where avatars went until Phase 9: the folder of the business in which they were uploaded.</summary>
    public static string LegacyAvatars(Guid tenantId) => $"avatars/{tenantId}";
}
