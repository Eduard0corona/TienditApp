using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FoodParkDigital.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Services;

/// <summary>
/// Private files in their own blob container (<c>FileStorage:AzureBlob:PrivateContainerName</c>, private-files by
/// default), created without public access; a container that allows anonymous reads is refused. Files uploaded before
/// they became private are read from the public container until the move brings them here.
/// </summary>
public sealed class AzureBlobPrivateFileStorage : IPrivateFileStorage, ILegacyPrivateFileMover
{
    public const string DefaultContainerName = "private-files";

    private readonly BlobContainerClient _private;
    private readonly BlobContainerClient _public;
    private readonly ILogger<AzureBlobPrivateFileStorage> _logger;
    private readonly SemaphoreSlim _containerLock = new(1, 1);
    private bool _containerReady;

    public AzureBlobPrivateFileStorage(IConfiguration configuration, ILogger<AzureBlobPrivateFileStorage> logger)
    {
        _logger = logger;

        var connectionString = configuration["FileStorage:AzureBlob:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("FileStorage:AzureBlob:ConnectionString no está configurado.");

        var publicName = configuration["FileStorage:AzureBlob:ContainerName"];
        if (string.IsNullOrWhiteSpace(publicName))
            throw new InvalidOperationException("FileStorage:AzureBlob:ContainerName no está configurado.");

        var privateName = configuration["FileStorage:AzureBlob:PrivateContainerName"];
        if (string.IsNullOrWhiteSpace(privateName))
            privateName = DefaultContainerName;
        if (string.Equals(privateName, publicName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("FileStorage:AzureBlob:PrivateContainerName debe ser distinto del contenedor público.");

        var service = new BlobServiceClient(connectionString);
        _private = service.GetBlobContainerClient(privateName);
        _public = service.GetBlobContainerClient(publicName);
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        if (!StorageNamespace.IsValidKey(key))
            throw new ArgumentException("La clave del archivo no es válida.", nameof(key));

        await EnsureContainerAsync(cancellationToken);
        await _private.GetBlobClient(key).UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
            },
            cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!StorageNamespace.IsValidKey(key))
            return null;

        return await DownloadAsync(_private, key, cancellationToken)
            ?? await DownloadAsync(_public, key, cancellationToken);
    }

    public async Task<int> MoveLegacyFilesAsync(Func<string, bool> isPrivateKey, CancellationToken cancellationToken)
    {
        await EnsureContainerAsync(cancellationToken);
        var moved = 0;
        await foreach (var blob in _public.GetBlobsAsync(prefix: "orders/", cancellationToken: cancellationToken))
        {
            if (!isPrivateKey(blob.Name) || !StorageNamespace.IsValidKey(blob.Name))
                continue;

            var source = _public.GetBlobClient(blob.Name);
            var destination = _private.GetBlobClient(blob.Name);
            if (!await destination.ExistsAsync(cancellationToken))
            {
                await using var content = (await source.DownloadStreamingAsync(cancellationToken: cancellationToken)).Value.Content;
                await destination.UploadAsync(
                    content,
                    new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = blob.Properties.ContentType } },
                    cancellationToken);
            }

            // Only once the private copy exists does the public one go away.
            await source.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
            moved++;
        }

        return moved;
    }

    private static async Task<Stream?> DownloadAsync(BlobContainerClient container, string key, CancellationToken cancellationToken)
    {
        try
        {
            var download = await container.GetBlobClient(key).DownloadStreamingAsync(cancellationToken: cancellationToken);
            return download.Value.Content;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (_containerReady)
            return;

        await _containerLock.WaitAsync(cancellationToken);
        try
        {
            if (!_containerReady)
            {
                await _private.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
                await RefusePublicContainerAsync(cancellationToken);
                _containerReady = true;
            }
        }
        finally
        {
            _containerLock.Release();
        }
    }

    // A container someone made public hands its files to anyone with their address: nothing private goes there.
    private async Task RefusePublicContainerAsync(CancellationToken cancellationToken)
    {
        PublicAccessType access;
        try
        {
            access = (await _private.GetAccessPolicyAsync(cancellationToken: cancellationToken)).Value.BlobPublicAccess;
        }
        catch (RequestFailedException ex) when (ex.Status == 403)
        {
            // Only the account key or Microsoft Entra can read the access level of a container.
            _logger.LogWarning("The access level of the private container {Container} could not be verified.", _private.Name);
            return;
        }

        if (access != PublicAccessType.None)
            throw new InvalidOperationException(
                $"El contenedor privado '{_private.Name}' permite lectura anónima; quítala antes de guardar archivos privados.");
    }
}
