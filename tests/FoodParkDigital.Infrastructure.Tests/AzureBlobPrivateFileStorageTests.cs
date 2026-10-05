using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.Extensions.Configuration;

namespace FoodParkDigital.Infrastructure.Tests;

public class AzureBlobPrivateFileStorageTests
{
    // Parsing a connection string makes no request.
    private const string OfflineAccount = "UseDevelopmentStorage=true";

    [Theory]
    [InlineData("uploads")]
    [InlineData("UPLOADS")]
    public void PrivateContainer_ShouldNotBeThePublicOne(string privateContainer)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Create(OfflineAccount, "uploads", privateContainer));

        Assert.Contains("PrivateContainerName", error.Message);
    }

    [Theory]
    [InlineData("../secreto.txt")]
    [InlineData("orders/../../secreto.txt")]
    [InlineData("/orders/a.webp")]
    [InlineData("secreto.txt")]
    public async Task KeysOutOfTheStorage_ShouldBeRefused_BeforeAnyRequest(string key)
    {
        var storage = Create(OfflineAccount, "uploads", null);

        await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveAsync(key, new MemoryStream([1]), "image/webp"));
        Assert.Null(await storage.OpenReadAsync(key));
    }

    internal static AzureBlobPrivateFileStorage Create(
        string connectionString,
        string publicContainer,
        string? privateContainer,
        CapturingLogger<AzureBlobPrivateFileStorage>? logger = null) =>
        new(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FileStorage:AzureBlob:ConnectionString"] = connectionString,
                    ["FileStorage:AzureBlob:ContainerName"] = publicContainer,
                    ["FileStorage:AzureBlob:PrivateContainerName"] = privateContainer
                })
                .Build(),
            logger ?? new CapturingLogger<AzureBlobPrivateFileStorage>());
}

/// <summary>Private files against a Blob Storage endpoint (Azurite in CI): a container nobody reads anonymously.</summary>
public sealed class AzureBlobPrivateFileStorageAzuriteTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _publicName = $"uploads-{Guid.NewGuid():N}"[..20];
    private readonly string _privateName = $"private-{Guid.NewGuid():N}"[..20];
    private BlobContainerClient _public = null!;
    private BlobContainerClient _private = null!;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(AzureBlobTestAccount.ConnectionString))
            return;

        var service = new BlobServiceClient(AzureBlobTestAccount.ConnectionString);
        _public = service.GetBlobContainerClient(_publicName);
        _private = service.GetBlobContainerClient(_privateName);
        await _public.CreateIfNotExistsAsync(PublicAccessType.Blob);
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(AzureBlobTestAccount.ConnectionString))
            return;

        await _public.DeleteIfExistsAsync();
        await _private.DeleteIfExistsAsync();
    }

    [AzureBlobFact]
    public async Task SavedFile_ShouldGoToAContainerWithoutAnonymousAccess()
    {
        var storage = Storage();
        var key = EvidenceKey();

        await storage.SaveAsync(key, new MemoryStream([1, 2, 3]), "image/webp");

        await using var content = await storage.OpenReadAsync(key);
        Assert.Equal([1, 2, 3], await ReadAllAsync(content!));
        Assert.Equal(PublicAccessType.None, (await _private.GetAccessPolicyAsync()).Value.BlobPublicAccess);
        Assert.Equal("image/webp", (await _private.GetBlobClient(key).GetPropertiesAsync()).Value.ContentType);
        Assert.False(await _public.GetBlobClient(key).ExistsAsync());
    }

    [AzureBlobFact]
    public async Task SavedFile_ShouldNeverBeOverwritten()
    {
        var storage = Storage();
        var key = EvidenceKey();
        await storage.SaveAsync(key, new MemoryStream([1]), "image/webp");

        await Assert.ThrowsAnyAsync<Azure.RequestFailedException>(
            () => storage.SaveAsync(key, new MemoryStream([2]), "image/webp"));

        await using var content = await storage.OpenReadAsync(key);
        Assert.Equal([1], await ReadAllAsync(content!));
    }

    [AzureBlobFact]
    public async Task PrivateContainerThatAllowsAnonymousReads_ShouldBeRefused()
    {
        await _private.CreateAsync(PublicAccessType.Blob);
        var storage = Storage();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.SaveAsync(EvidenceKey(), new MemoryStream([1]), "image/webp"));

        Assert.Contains(_privateName, error.Message);
        Assert.Empty(await ListAsync(_private));
        // Nor does it move the public files there.
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.MoveLegacyFilesAsync(IsPrivateKey, CancellationToken.None));
    }

    [AzureBlobFact]
    public async Task FileUploadedBeforeItBecamePrivate_ShouldOpenUntilItMoves_ThenOnlyFromThePrivateContainer()
    {
        var storage = Storage();
        var key = EvidenceKey();
        await _public.GetBlobClient(key).UploadAsync(
            new BinaryData(new byte[] { 7, 7 }), new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "image/webp" } });
        var productImage = $"products/{_tenantId}/foto.webp";
        await _public.GetBlobClient(productImage).UploadAsync(new BinaryData(new byte[] { 9 }));

        await using (var beforeMove = await storage.OpenReadAsync(key))
            Assert.Equal([7, 7], await ReadAllAsync(beforeMove!));

        var moved = await storage.MoveLegacyFilesAsync(IsPrivateKey, CancellationToken.None);
        var movedAgain = await storage.MoveLegacyFilesAsync(IsPrivateKey, CancellationToken.None);

        Assert.Equal(1, moved);
        Assert.Equal(0, movedAgain);
        Assert.Equal([productImage], await ListAsync(_public));
        Assert.Equal([key], await ListAsync(_private));
        Assert.Equal("image/webp", (await _private.GetBlobClient(key).GetPropertiesAsync()).Value.ContentType);
        await using var afterMove = await storage.OpenReadAsync(key);
        Assert.Equal([7, 7], await ReadAllAsync(afterMove!));
    }

    [AzureBlobFact]
    public async Task MissingFile_ShouldOpenAsNothing()
    {
        Assert.Null(await Storage().OpenReadAsync(EvidenceKey()));
    }

    private AzureBlobPrivateFileStorage Storage() =>
        AzureBlobPrivateFileStorageTests.Create(AzureBlobTestAccount.ConnectionString!, _publicName, _privateName);

    private string EvidenceKey() => $"orders/{_tenantId}/payment-evidence/202610/{Guid.NewGuid()}.webp";

    private static bool IsPrivateKey(string key) => OrderUploadRules.TryParsePrivateKey(key, out _, out _);

    private static async Task<List<string>> ListAsync(BlobContainerClient container)
    {
        var names = new List<string>();
        await foreach (var blob in container.GetBlobsAsync())
            names.Add(blob.Name);
        return names;
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }
}
