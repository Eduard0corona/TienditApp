using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Services;

public class LocalFileStorageService : IFileStorageService
{
    private readonly string _basePath;
    private readonly ILogger<LocalFileStorageService> _logger;
    private const string UploadFolder = "uploads";
    private const string UrlPrefix = $"/{UploadFolder}/";

    private static readonly string[] VariantSuffixes =
        ["_original", "_lg", "_md", "_sm", "_xs"];

    public LocalFileStorageService(IWebHostEnvironment env, ILogger<LocalFileStorageService> logger)
    {
        _basePath = Path.Combine(env.ContentRootPath, "wwwroot", UploadFolder);
        _logger = logger;
    }

    public async Task<string> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default)
    {
        // Callers build both from the owner and a generated name; anything else could write outside the folder.
        StorageNamespace.EnsureValidFolder(folder);
        StorageNamespace.EnsureValidFileName(fileName);
        var folderPath = Path.Combine(_basePath, folder);
        Directory.CreateDirectory(folderPath);

        var filePath = Path.Combine(folderPath, fileName);
        await using var fs = new FileStream(filePath, FileMode.Create);
        await fileStream.CopyToAsync(fs, cancellationToken);

        return $"/{UploadFolder}/{folder}/{fileName}";
    }

    public Task DeleteAsync(string fileUrl, string folder, CancellationToken cancellationToken = default)
    {
        if (TryResolvePath(fileUrl, folder, out var fullPath))
            DeleteIfExists(fullPath);

        return Task.CompletedTask;
    }

    public Task DeleteVariantsAsync(string baseImageUrl, string folder, CancellationToken cancellationToken = default)
    {
        if (!TryResolvePath(baseImageUrl, folder, out var basePath))
            return Task.CompletedTask;

        // The suffixes contain no separators, so every variant stays next to the validated base path.
        foreach (var suffix in VariantSuffixes)
            DeleteIfExists($"{basePath}{suffix}.webp");

        return Task.CompletedTask;
    }

    public bool IsStoredIn(string fileUrl, string folder) => Resolve(fileUrl, folder, out _);

    public Task<Stream?> OpenReadAsync(string fileUrl, string folder, CancellationToken cancellationToken = default)
    {
        if (!Resolve(fileUrl, folder, out var fullPath) || !File.Exists(fullPath))
            return Task.FromResult<Stream?>(null);
        return Task.FromResult<Stream?>(new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    private bool TryResolvePath(string fileUrl, string folder, out string fullPath)
    {
        if (Resolve(fileUrl, folder, out fullPath))
            return true;

        if (!string.IsNullOrEmpty(fileUrl))
            _logger.LogWarning("Refused to delete a file outside the storage folder {Folder}.", folder);
        return false;
    }

    // Maps "/uploads/{folder}/..." to a path below the folder's directory; any other URL is refused.
    private bool Resolve(string fileUrl, string folder, out string fullPath)
    {
        StorageNamespace.EnsureValidFolder(folder);
        fullPath = string.Empty;

        if (string.IsNullOrEmpty(fileUrl)
            || !fileUrl.StartsWith(UrlPrefix, StringComparison.Ordinal)
            || !StorageNamespace.IsInsideFolder(fileUrl[UrlPrefix.Length..], folder))
            return false;

        // Defense in depth: the normalized absolute path must still sit below the folder's directory.
        var folderPath = Path.GetFullPath(Path.Combine(_basePath, folder)) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(_basePath, fileUrl[UrlPrefix.Length..]));
        if (!candidate.StartsWith(folderPath, StringComparison.Ordinal))
            return false;

        fullPath = candidate;
        return true;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
