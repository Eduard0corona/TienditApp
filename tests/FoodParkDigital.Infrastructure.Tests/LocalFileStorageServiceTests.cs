using FoodParkDigital.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Infrastructure.Tests;

public sealed class LocalFileStorageServiceTests : IDisposable
{
    private static readonly string[] VariantSuffixes = ["_original", "_lg", "_md", "_sm", "_xs"];

    private readonly Guid _ownTenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly string _contentRoot =
        Path.Combine(Path.GetTempPath(), "fpd-local-storage", Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorageService _storage;

    public LocalFileStorageServiceTests()
    {
        Directory.CreateDirectory(_contentRoot);
        _storage = new LocalFileStorageService(
            new TestWebHostEnvironment(_contentRoot),
            NullLogger<LocalFileStorageService>.Instance);
    }

    private string OwnFolder => $"products/{_ownTenantId}";

    private string UploadsRoot => Path.Combine(_contentRoot, "wwwroot", "uploads");

    // URL recibida y ruta base, relativa a uploads, de los archivos a los que apuntaría sin validación.
    public static TheoryData<string, string> UrlsOutsideOwnFolder => new()
    {
        { "/uploads/products/{other}/image", "products/{other}/image" },
        { "/uploads/products/{own}/../{other}/image", "products/{other}/image" },
        { "/uploads/products/{own}/./../{other}/image", "products/{other}/image" },
        { "/uploads/products/{own}/..\\{other}/image", "products/{other}/image" },
        { "/uploads/products/{own}/%2e%2e/{other}/image", "products/{other}/image" },
        { "uploads/products/{other}/image", "products/{other}/image" },
        { "/uploads/products/{own}-copia/image", "products/{own}-copia/image" },
        { "/uploads/logos/{own}/image", "logos/{own}/image" },
        { "/uploads/products/{own}", "products/{own}" },
        { "/uploads/../image", "../image" },
        { "/uploads/products/{own}/../../../../appsettings", "../../appsettings" }
    };

    [Theory]
    [MemberData(nameof(UrlsOutsideOwnFolder))]
    public async Task DeleteVariantsAsync_ShouldKeepFiles_WhenUrlResolvesOutsideFolder(
        string urlTemplate,
        string targetTemplate)
    {
        var targetFiles = SeedVariants(Resolve(targetTemplate));

        await _storage.DeleteVariantsAsync(Resolve(urlTemplate), OwnFolder);

        Assert.All(targetFiles, path => Assert.True(File.Exists(path), $"Se borró {path}"));
    }

    [Fact]
    public async Task DeleteVariantsAsync_ShouldDeleteVariants_WhenUrlIsInsideFolder()
    {
        var ownFiles = SeedVariants($"{OwnFolder}/image");
        var unrelatedFile = SeedFile($"{OwnFolder}/image-2_original.webp");

        await _storage.DeleteVariantsAsync($"/uploads/{OwnFolder}/image", OwnFolder);

        Assert.All(ownFiles, path => Assert.False(File.Exists(path), $"No se borró {path}"));
        Assert.True(File.Exists(unrelatedFile));
    }

    [Fact]
    public async Task DeleteVariantsAsync_ShouldDeleteFilesUploadedByUploadAsync()
    {
        string? originalUrl = null;
        foreach (var suffix in VariantSuffixes)
        {
            var url = await _storage.UploadAsync(
                new MemoryStream([0x52, 0x49, 0x46, 0x46]),
                $"image{suffix}.webp",
                "image/webp",
                OwnFolder);
            originalUrl ??= url;
        }

        var baseUrl = originalUrl![..^"_original.webp".Length];
        await _storage.DeleteVariantsAsync(baseUrl, OwnFolder);

        Assert.Empty(Directory.GetFiles(Path.Combine(UploadsRoot, OwnFolder)));
    }

    [Fact]
    public async Task DeleteAsync_ShouldOnlyDeleteFilesInsideFolder()
    {
        var ownFile = SeedFile($"{OwnFolder}/202610/evidencia.pdf");
        var otherTenantFile = SeedFile($"products/{_otherTenantId}/evidencia.pdf");

        await _storage.DeleteAsync($"/uploads/{OwnFolder}/202610/evidencia.pdf", OwnFolder);
        await _storage.DeleteAsync($"/uploads/products/{_otherTenantId}/evidencia.pdf", OwnFolder);
        await _storage.DeleteAsync($"/uploads/{OwnFolder}/../{_otherTenantId}/evidencia.pdf", OwnFolder);

        Assert.False(File.Exists(ownFile));
        Assert.True(File.Exists(otherTenantFile));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/products/tenant")]
    [InlineData("products/tenant/")]
    [InlineData("products//tenant")]
    [InlineData("products/../tenant")]
    [InlineData("products/./tenant")]
    [InlineData("products\\tenant")]
    public async Task DeleteVariantsAsync_ShouldThrow_WhenFolderIsNotCanonical(string folder)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _storage.DeleteVariantsAsync("/uploads/products/tenant/image", folder));
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
    }

    private string Resolve(string template) => template
        .Replace("{own}", _ownTenantId.ToString())
        .Replace("{other}", _otherTenantId.ToString());

    private IReadOnlyList<string> SeedVariants(string basePath) => VariantSuffixes
        .Select(suffix => SeedFile($"{basePath}{suffix}.webp"))
        .ToList();

    private string SeedFile(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(UploadsRoot, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x52, 0x49, 0x46, 0x46]);
        return path;
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
