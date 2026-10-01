using FoodParkDigital.Infrastructure.Services;

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

    private string Resolve(string template) => template
        .Replace("{own}", _ownTenantId.ToString())
        .Replace("{other}", _otherTenantId.ToString());
}
