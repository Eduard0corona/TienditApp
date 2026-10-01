namespace FoodParkDigital.Application.Common.Interfaces;

public interface IFileStorageService
{
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
}
