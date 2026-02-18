using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace FoodParkDigital.Infrastructure.Services;

public class AzureBlobFileStorageService : IFileStorageService
{
    private static readonly string[] VariantSuffixes =
        ["_original", "_lg", "_md", "_sm", "_xs"];

    private readonly BlobContainerClient _containerClient;

    public AzureBlobFileStorageService(IConfiguration configuration)
    {
        var connectionString = configuration["FileStorage:AzureBlob:ConnectionString"];
        var containerName = configuration["FileStorage:AzureBlob:ContainerName"];

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("FileStorage:AzureBlob:ConnectionString no esta configurado.");

        if (string.IsNullOrWhiteSpace(containerName))
            throw new InvalidOperationException("FileStorage:AzureBlob:ContainerName no esta configurado.");

        var blobServiceClient = new BlobServiceClient(connectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
    }

    public async Task<string> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default)
    {
        await _containerClient.CreateIfNotExistsAsync(
            PublicAccessType.Blob,
            cancellationToken: cancellationToken);

        var safeFolder = folder.Trim('/').Replace("\\", "/");
        var blobName = $"{safeFolder}/{fileName}";
        var blobClient = _containerClient.GetBlobClient(blobName);

        await blobClient.UploadAsync(
            fileStream,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType
                }
            },
            cancellationToken);

        return blobClient.Uri.ToString();
    }

    public async Task DeleteAsync(string fileUrl, CancellationToken cancellationToken = default)
    {
        if (!TryResolveBlobName(fileUrl, out var blobName))
            return;

        var blobClient = _containerClient.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots,
            cancellationToken: cancellationToken);
    }

    public async Task DeleteVariantsAsync(string baseImageUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseImageUrl))
            return;

        foreach (var suffix in VariantSuffixes)
        {
            await DeleteAsync($"{baseImageUrl}{suffix}.webp", cancellationToken);
        }
    }

    private bool TryResolveBlobName(string fileUrl, out string blobName)
    {
        blobName = string.Empty;
        if (string.IsNullOrWhiteSpace(fileUrl))
            return false;

        if (Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri))
        {
            var path = uri.AbsolutePath.TrimStart('/');
            var containerPrefix = $"{_containerClient.Name}/";
            if (path.StartsWith(containerPrefix, StringComparison.OrdinalIgnoreCase))
                path = path[containerPrefix.Length..];

            blobName = Uri.UnescapeDataString(path);
            return !string.IsNullOrWhiteSpace(blobName);
        }

        var relativePath = fileUrl.TrimStart('/');
        if (relativePath.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
            relativePath = relativePath["uploads/".Length..];

        blobName = relativePath.Replace("\\", "/");
        return !string.IsNullOrWhiteSpace(blobName);
    }
}
