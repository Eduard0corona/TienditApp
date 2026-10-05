using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Identity;

/// <summary>
/// The files of an account's avatar live in the account's folder; older ones, in the folder of the business where they
/// were uploaded, or of <see cref="Guid.Empty"/> when the session had no business. They are deleted from whichever of
/// those folders holds them, folders taken from the account (its id and its memberships), never from the URL: an avatar
/// URL pointing elsewhere deletes nothing. Only the upload writes an avatar URL, so one in a shared legacy folder is the
/// account's own file.
/// </summary>
internal static class AvatarFiles
{
    public static async Task DeleteAsync(
        IFileStorageService storage,
        IApplicationDbContext context,
        Guid userId,
        string avatarUrl,
        CancellationToken cancellationToken)
    {
        var folders = new List<string>
        {
            TenantStorageFolders.UserAvatars(userId),
            TenantStorageFolders.LegacyAvatars(Guid.Empty)
        };
        // The person's own memberships, in every business: an avatar uploaded in one of them is still theirs.
        folders.AddRange((await context.TenantUsers
                .IgnoreQueryFilters()
                .Where(m => m.UserId == userId)
                .Select(m => m.TenantId)
                .ToListAsync(cancellationToken))
            .Select(TenantStorageFolders.LegacyAvatars));

        var folder = folders.FirstOrDefault(candidate => storage.IsStoredIn(avatarUrl, candidate));
        if (folder is not null)
            await storage.DeleteVariantsAsync(avatarUrl, folder, cancellationToken);
    }
}
