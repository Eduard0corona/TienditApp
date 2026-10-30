namespace FoodParkDigital.Application.Common.Interfaces;

public interface IFileStorageService
{
    /// <summary>
    /// Stores the file as <paramref name="folder"/>/<paramref name="fileName"/> and returns its URL. The folder is the
    /// owner's (<c>products/{tenantId}</c>) and the name a single generated segment: anything else (separators,
    /// traversal, encoded characters) is refused with <see cref="ArgumentException"/>.
    /// </summary>
    Task<string> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the file only when <paramref name="fileUrl"/> resolves inside <paramref name="folder"/>
    /// (for example <c>products/{tenantId}</c>). URLs outside that folder, including traversal attempts,
    /// are ignored. Callers must derive the folder from the owning tenant, never from the URL.
    /// </summary>
    Task DeleteAsync(string fileUrl, string folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the image variants of <paramref name="baseImageUrl"/> with the same folder rules as
    /// <see cref="DeleteAsync"/>.
    /// </summary>
    Task DeleteVariantsAsync(string baseImageUrl, string folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether <paramref name="fileUrl"/> names a file this storage keeps inside <paramref name="folder"/>, by the same
    /// rules as <see cref="DeleteAsync"/> and without touching the storage.
    /// </summary>
    bool IsStoredIn(string fileUrl, string folder);

    /// <summary>
    /// Opens a file this storage keeps inside <paramref name="folder"/>, by the same rules as <see cref="DeleteAsync"/>; null
    /// when the URL resolves outside that folder or the file is not there.
    /// </summary>
    Task<Stream?> OpenReadAsync(string fileUrl, string folder, CancellationToken cancellationToken = default);
}
