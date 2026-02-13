namespace FoodParkDigital.Application.Common.Interfaces;

public interface IFileStorageService
{
    Task<string> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string fileUrl, CancellationToken cancellationToken = default);

    Task DeleteVariantsAsync(string baseImageUrl, CancellationToken cancellationToken = default);
}
