using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace FoodParkDigital.Infrastructure.Services;

/// <summary>
/// Private files on disk, outside wwwroot so the static file middleware never serves them
/// (<c>FileStorage:Local:PrivateRootPath</c>, App_Data/private-files by default). Files uploaded before they became
/// private are read from wwwroot/uploads until the move brings them here.
/// </summary>
public sealed class LocalPrivateFileStorage : IPrivateFileStorage, ILegacyPrivateFileMover
{
    private const int BufferSize = 81920;

    private readonly string _root;
    private readonly string _legacyRoot;

    public LocalPrivateFileStorage(IWebHostEnvironment environment, IConfiguration configuration)
    {
        var configured = configuration["FileStorage:Local:PrivateRootPath"];
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "private-files")
            : configured);
        _legacyRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "wwwroot", "uploads"));

        // Whatever is under the web root is served to anyone as a static file.
        var webRoots = new[] { environment.WebRootPath, Path.Combine(environment.ContentRootPath, "wwwroot") };
        if (webRoots.Any(webRoot => !string.IsNullOrWhiteSpace(webRoot) && IsSameOrInside(_root, Path.GetFullPath(webRoot))))
            throw new InvalidOperationException("FileStorage:Local:PrivateRootPath no puede estar dentro de wwwroot.");
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var path = Resolve(_root, key) ?? throw new ArgumentException("La clave del archivo no es válida.", nameof(key));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        foreach (var root in new[] { _root, _legacyRoot })
        {
            var path = Resolve(root, key);
            if (path is not null && File.Exists(path))
                return Task.FromResult<Stream?>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true));
        }

        return Task.FromResult<Stream?>(null);
    }

    public Task<int> MoveLegacyFilesAsync(Func<string, bool> isPrivateKey, CancellationToken cancellationToken)
    {
        var moved = 0;
        var legacyOrders = Path.Combine(_legacyRoot, "orders");
        if (!Directory.Exists(legacyOrders))
            return Task.FromResult(moved);

        // Listed first: files move out of the folder being walked.
        foreach (var file in Directory.GetFiles(legacyOrders, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = Path.GetRelativePath(_legacyRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            if (!isPrivateKey(key) || Resolve(_root, key) is not { } destination)
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            // A copy that arrived before (an earlier move stopped halfway) wins: the public one only goes away.
            if (File.Exists(destination))
                File.Delete(file);
            else
                File.Move(file, destination);
            moved++;
        }

        return Task.FromResult(moved);
    }

    // A key is a canonical relative path ("orders/{tenant}/..."): the file it names must stay below the root.
    private static string? Resolve(string root, string key)
    {
        if (!StorageNamespace.IsValidKey(key))
            return null;

        var path = Path.GetFullPath(Path.Combine(root, key));
        return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? path : null;
    }

    // Case-insensitive so that a differently written path on Windows or macOS is not taken as another folder.
    private static bool IsSameOrInside(string path, string folder)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(folder);
        return string.Equals(Path.TrimEndingDirectorySeparator(path), trimmed, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(trimmed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
