using FoodParkDigital.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Infrastructure.Tests;

public class AzureBlobFileStorageServiceTests
{
    private static readonly Uri ContainerUri = new("https://tienditatest.blob.core.windows.net/uploads");

    private readonly Guid _ownTenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();

    private string OwnFolder => $"products/{_ownTenantId}";

    [Theory]
    [InlineData("https://tienditatest.blob.core.windows.net/uploads/products/{own}/image")]
    [InlineData("https://TIENDITATEST.blob.core.windows.net:443/uploads/products/{own}/image")]
    [InlineData("https://tienditatest.blob.core.windows.net/uploads/products/{own}/image?v=2")]
    [InlineData("/uploads/products/{own}/image")]
    public void TryResolveBlobName_ShouldReturnBlobName_WhenUrlIsInsideFolder(string urlTemplate)
    {
        var resolved = AzureBlobFileStorageService.TryResolveBlobName(
            ContainerUri, Resolve(urlTemplate), OwnFolder, out var blobName);

        Assert.True(resolved);
        Assert.Equal($"{OwnFolder}/image", blobName);
    }

    [Theory]
    // Otro tenant, otra área o una carpeta que solo comparte prefijo.
    [InlineData("https://tienditatest.blob.core.windows.net/uploads/products/{other}/image")]
    [InlineData("https://tienditatest.blob.core.windows.net/uploads/logos/{own}/image")]
    [InlineData("https://tienditatest.blob.core.windows.net/uploads/products/{own}-copia/image")]
    [InlineData("https://tienditatest.blob.core.windows.net/uploads/products/{own}")]
    // Traversal literal, codificado o con separadores codificados.
    [InlineData("https://tienditatest.blob.core.windows.net/uploads/products/{own}/../{other}/image")]
    [InlineData("https://tienditatest.blob.core.windows.net/uploads/products/{own}/%2E%2E/{other}/image")]
    [InlineData("https://tienditatest.blob.core.windows.net/uploads/products/{own}%2F..%2F{other}/image")]
    [InlineData("https://tienditatest.blob.core.windows.net/uploads/products/{own}/..%5C{other}/image")]
    [InlineData("/uploads/products/{own}/../{other}/image")]
    [InlineData("/uploads/products/{other}/image")]
    // URLs que no pertenecen a este contenedor.
    [InlineData("https://otracuenta.blob.core.windows.net/uploads/products/{own}/image")]
    [InlineData("https://tienditatest.blob.core.windows.net@otracuenta.blob.core.windows.net/uploads/products/{own}/image")]
    [InlineData("http://tienditatest.blob.core.windows.net/uploads/products/{own}/image")]
    [InlineData("https://tienditatest.blob.core.windows.net/respaldos/products/{own}/image")]
    [InlineData("https://tienditatest.blob.core.windows.net/uploads-old/products/{own}/image")]
    [InlineData("file:///uploads/products/{own}/image")]
    [InlineData("products/{own}/image")]
    [InlineData("")]
    public void TryResolveBlobName_ShouldRefuse_WhenUrlIsOutsideFolderOrContainer(string urlTemplate)
    {
        var resolved = AzureBlobFileStorageService.TryResolveBlobName(
            ContainerUri, Resolve(urlTemplate), OwnFolder, out var blobName);

        Assert.False(resolved);
        Assert.Empty(blobName);
    }

    [Fact]
    public void TryResolveBlobName_ShouldUseFullContainerPath_ForPathStyleEndpoints()
    {
        // Azurite y endpoints path-style incluyen la cuenta en la ruta del contenedor.
        var containerUri = new Uri("http://127.0.0.1:10000/devstoreaccount1/uploads");

        var ownResolved = AzureBlobFileStorageService.TryResolveBlobName(
            containerUri,
            $"http://127.0.0.1:10000/devstoreaccount1/uploads/{OwnFolder}/image",
            OwnFolder,
            out var blobName);
        var otherAccountResolved = AzureBlobFileStorageService.TryResolveBlobName(
            containerUri,
            $"http://127.0.0.1:10000/otracuenta/uploads/{OwnFolder}/image",
            OwnFolder,
            out _);

        Assert.True(ownResolved);
        Assert.Equal($"{OwnFolder}/image", blobName);
        Assert.False(otherAccountResolved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/products/tenant")]
    [InlineData("products/tenant/")]
    [InlineData("products/../tenant")]
    [InlineData("products\\tenant")]
    public void TryResolveBlobName_ShouldThrow_WhenFolderIsNotCanonical(string folder)
    {
        Assert.Throws<ArgumentException>(() => AzureBlobFileStorageService.TryResolveBlobName(
            ContainerUri, "/uploads/products/tenant/image", folder, out _));
    }

    [Theory]
    [InlineData("products/../..", "imagen.webp")]
    [InlineData("/products/tenant", "imagen.webp")]
    [InlineData("products/tenant", "../imagen.webp")]
    [InlineData("products/tenant", "sub/imagen.webp")]
    [InlineData("products/tenant", "imagen%2Fwebp")]
    public async Task UploadAsync_ShouldRefuse_FoldersAndNamesThatCouldLeaveTheFolder(string folder, string fileName)
    {
        // Refused before reaching the storage: the emulator's address is never called.
        await Assert.ThrowsAsync<ArgumentException>(() => Storage().UploadAsync(
            new MemoryStream([0x52, 0x49, 0x46, 0x46]), fileName, "image/webp", folder));
    }

    [Fact]
    public void IsStoredIn_ShouldFollowTheRulesOfDeleting()
    {
        var storage = Storage();
        var container = "http://127.0.0.1:10000/devstoreaccount1/uploads";

        Assert.True(storage.IsStoredIn($"{container}/{OwnFolder}/image", OwnFolder));
        Assert.False(storage.IsStoredIn($"{container}/products/{_otherTenantId}/image", OwnFolder));
        Assert.False(storage.IsStoredIn($"{container}/{OwnFolder}/../{_otherTenantId}/image", OwnFolder));
    }

    private static AzureBlobFileStorageService Storage() => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileStorage:AzureBlob:ConnectionString"] = "UseDevelopmentStorage=true",
            ["FileStorage:AzureBlob:ContainerName"] = "uploads"
        }).Build(),
        NullLogger<AzureBlobFileStorageService>.Instance);

    private string Resolve(string template) => template
        .Replace("{own}", _ownTenantId.ToString())
        .Replace("{other}", _otherTenantId.ToString());
}
