using FoodParkDigital.Application.Orders;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>Private files live outside wwwroot, so no static path serves them.</summary>
public sealed class LocalPrivateFileStorageTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _contentRoot =
        Path.Combine(Path.GetTempPath(), "fpd-private-storage", Guid.NewGuid().ToString("N"));
    private readonly LocalPrivateFileStorage _storage;

    public LocalPrivateFileStorageTests()
    {
        Directory.CreateDirectory(_contentRoot);
        _storage = new LocalPrivateFileStorage(new TestWebHostEnvironment(_contentRoot), new ConfigurationBuilder().Build());
    }

    private string WebRoot => Path.Combine(_contentRoot, "wwwroot");

    [Fact]
    public async Task SavedFile_ShouldOpenByItsKey_AndStayOutOfWwwroot()
    {
        var key = EvidenceKey();

        await _storage.SaveAsync(key, new MemoryStream([1, 2, 3]), "image/webp");

        await using var content = await _storage.OpenReadAsync(key);
        Assert.NotNull(content);
        Assert.Equal([1, 2, 3], await ReadAllAsync(content!));
        Assert.True(File.Exists(Path.Combine(_contentRoot, "App_Data", "private-files", key)));
        Assert.Empty(Directory.Exists(WebRoot) ? Directory.GetFiles(WebRoot, "*", SearchOption.AllDirectories) : []);
    }

    [Theory]
    [InlineData("wwwroot")]
    [InlineData("wwwroot/private")]
    [InlineData("WWWROOT/uploads/")]
    public void PrivateRootInsideTheWebRoot_ShouldBeRefused(string configured)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:Local:PrivateRootPath"] = Path.Combine(_contentRoot, configured)
            })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(
            () => new LocalPrivateFileStorage(new TestWebHostEnvironment(_contentRoot), configuration));

        Assert.Contains("wwwroot", error.Message);
    }

    [Fact]
    public async Task ConfiguredPrivateRoot_ShouldKeepTheFiles()
    {
        var root = Path.Combine(_contentRoot, "datos", "privados");
        var storage = new LocalPrivateFileStorage(
            new TestWebHostEnvironment(_contentRoot),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["FileStorage:Local:PrivateRootPath"] = root })
                .Build());
        var key = EvidenceKey();

        await storage.SaveAsync(key, new MemoryStream([4]), "image/webp");

        Assert.True(File.Exists(Path.Combine(root, key)));
    }

    [Theory]
    [InlineData("../secreto.txt")]
    [InlineData("orders/../../secreto.txt")]
    [InlineData("orders/a\\..\\..\\secreto.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("secreto.txt")]
    public async Task KeysOutOfTheStorage_ShouldBeRefused(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.SaveAsync(key, new MemoryStream([1]), "image/webp"));
        Assert.Null(await _storage.OpenReadAsync(key));
    }

    [Fact]
    public async Task FileUploadedBeforeItBecamePrivate_ShouldOpenUntilItMoves_ThenOnlyFromPrivateStorage()
    {
        var key = EvidenceKey();
        var publicCopy = Path.Combine(WebRoot, "uploads", key);
        Directory.CreateDirectory(Path.GetDirectoryName(publicCopy)!);
        await File.WriteAllBytesAsync(publicCopy, [7, 7]);
        var productImage = Path.Combine(WebRoot, "uploads", "products", _tenantId.ToString(), "foto.webp");
        Directory.CreateDirectory(Path.GetDirectoryName(productImage)!);
        await File.WriteAllBytesAsync(productImage, [9]);

        await using (var beforeMove = await _storage.OpenReadAsync(key))
            Assert.Equal([7, 7], await ReadAllAsync(beforeMove!));

        var moved = await _storage.MoveLegacyFilesAsync(IsPrivateKey, CancellationToken.None);
        var movedAgain = await _storage.MoveLegacyFilesAsync(IsPrivateKey, CancellationToken.None);

        Assert.Equal(1, moved);
        Assert.Equal(0, movedAgain);
        Assert.False(File.Exists(publicCopy));
        // Public files of the catalog stay where they are.
        Assert.True(File.Exists(productImage));
        await using var afterMove = await _storage.OpenReadAsync(key);
        Assert.Equal([7, 7], await ReadAllAsync(afterMove!));
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
    }

    private string EvidenceKey() => $"orders/{_tenantId}/payment-evidence/202610/{Guid.NewGuid()}.webp";

    private static bool IsPrivateKey(string key) => OrderUploadRules.TryParsePrivateKey(key, out _, out _);

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    private sealed class TestWebHostEnvironment(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "FoodParkDigital.Infrastructure.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Testing";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(contentRootPath, "wwwroot");
    }
}
