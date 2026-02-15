using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;

namespace FoodParkDigital.Infrastructure.Services;

public class LocalFileStorageService : IFileStorageService
{
    private readonly string _basePath;
    private const string UploadFolder = "uploads";

    private static readonly string[] VariantSuffixes =
        ["_original", "_lg", "_md", "_sm", "_xs"];

    public LocalFileStorageService(IWebHostEnvironment env)
    {
        _basePath = Path.Combine(env.ContentRootPath, "wwwroot", UploadFolder);
    }

    public async Task<string> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default)
    {
        var folderPath = Path.Combine(_basePath, folder);
        Directory.CreateDirectory(folderPath);

        var filePath = Path.Combine(folderPath, fileName);
        await using var fs = new FileStream(filePath, FileMode.Create);
        await fileStream.CopyToAsync(fs, cancellationToken);

        return $"/{UploadFolder}/{folder}/{fileName}";
    }

    public Task DeleteAsync(string fileUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(fileUrl))
            return Task.CompletedTask;

        var relativePath = fileUrl.TrimStart('/');
        var fullPath = Path.Combine(
            Directory.GetParent(_basePath)!.FullName, relativePath);

        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    public Task DeleteVariantsAsync(string baseImageUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(baseImageUrl))
            return Task.CompletedTask;

        var relativePath = baseImageUrl.TrimStart('/');
        var wwwrootPath = Directory.GetParent(_basePath)!.FullName;

        foreach (var suffix in VariantSuffixes)
        {
            var fullPath = Path.Combine(wwwrootPath, $"{relativePath}{suffix}.webp");
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }
}
