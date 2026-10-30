using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Rules;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Invoicing;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>The logo that goes on the PDF of a business's CFDI (Fase 28L): its own, from its folder, or none.</summary>
public sealed class CfdiPdfLogosTests : IDisposable
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), "fpd-cfdi-logos", Guid.NewGuid().ToString("N"));
    private readonly ImageProcessingService _images = new();
    private readonly LocalFileStorageService _storage;
    private readonly ApplicationDbContext _context = InMemoryApplicationDbContext.Create();
    private readonly CapturingLogger<TenantCfdiPdfLogos> _logger = new();
    private readonly TenantCfdiPdfLogos _logos;

    public CfdiPdfLogosTests()
    {
        Directory.CreateDirectory(_contentRoot);
        _storage = new LocalFileStorageService(new WebHost(_contentRoot), NullLogger<LocalFileStorageService>.Instance);
        var services = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(_context)
            .AddSingleton<IFileStorageService>(_storage)
            .BuildServiceProvider();
        _logos = new TenantCfdiPdfLogos(services.GetRequiredService<IServiceScopeFactory>(), _images, _logger);
    }

    [Fact]
    public async Task EachBusiness_ShouldGetItsOwnLogo_AsPngWithinTheTemplatesBox()
    {
        var first = await BusinessAsync("Taquería");
        await GiveLogoAsync(first, 1200, 300, Color.DarkRed);
        var second = await BusinessAsync("Estética");
        await GiveLogoAsync(second, 300, 600, Color.Navy);

        var firstPng = await _logos.PngAsync(first.Id, CancellationToken.None);
        var secondPng = await _logos.PngAsync(second.Id, CancellationToken.None);

        // The medium variant (512 px wide), fitted into 400 x 200 keeping its proportions.
        using var firstImage = AssertPng(firstPng);
        Assert.Equal((400, 100), (firstImage.Width, firstImage.Height));
        Assert.True(firstImage[200, 50] is { R: > 100, B: < 40 }, "The first business got another logo.");
        using var secondImage = AssertPng(secondPng);
        Assert.Equal((100, 200), (secondImage.Width, secondImage.Height));
        Assert.True(secondImage[50, 100] is { B: > 90, R: < 40 }, "The second business got another logo.");
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Logo_ShouldKeepItsTransparency()
    {
        var business = await BusinessAsync("Taquería");
        await GiveLogoAsync(business, 400, 200, Color.Transparent);

        using var image = AssertPng(await _logos.PngAsync(business.Id, CancellationToken.None));

        Assert.Equal(0, image[10, 10].A);
    }

    [Fact]
    public async Task BusinessWithoutLogo_ShouldGetNone_WithoutReadingTheStorage()
    {
        var business = await BusinessAsync("Taquería");

        Assert.Null(await _logos.PngAsync(business.Id, CancellationToken.None));
        Assert.Null(await _logos.PngAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Empty(_logger.Entries);
        Assert.False(Directory.Exists(Path.Combine(_contentRoot, "wwwroot", "uploads")));
    }

    [Fact]
    public async Task LogoUrlOfAnotherBusiness_ShouldNeverPutItsLogoOnThePdf()
    {
        var owner = await BusinessAsync("Estética");
        var ownerLogo = await GiveLogoAsync(owner, 400, 200, Color.Navy);
        var copier = await BusinessAsync("Taquería");
        // A URL that points to the other business's folder, as is or with a detour through its own.
        foreach (var url in new[] { ownerLogo, ownerLogo.Replace(owner.Id.ToString(), $"{copier.Id}/../{owner.Id}") })
        {
            copier.Update(copier.Name, copier.ContactEmail, copier.ContactPhone, url);
            await _context.SaveChangesAsync();

            Assert.Null(await _logos.PngAsync(copier.Id, CancellationToken.None));
        }

        Assert.NotNull(await _logos.PngAsync(owner.Id, CancellationToken.None));
        Assert.All(_logger.Entries, entry =>
        {
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Equal(copier.Id, entry["TenantId"]);
            Assert.False(entry.Mentions(owner.Id.ToString()), "The log names the other business's folder.");
        });
        Assert.Equal(2, _logger.Entries.Count);
    }

    [Fact]
    public async Task LogoThatIsGoneOrIsNotAnImage_ShouldLeaveThePdfWithoutIt()
    {
        var gone = await BusinessAsync("Taquería");
        gone.Update(gone.Name, gone.ContactEmail, gone.ContactPhone, $"/uploads/{TenantStorageFolders.Logos(gone.Id)}/{Guid.NewGuid()}");
        var broken = await BusinessAsync("Estética");
        var brokenBase = $"{TenantStorageFolders.Logos(broken.Id)}/{Guid.NewGuid()}";
        await _storage.UploadAsync(new MemoryStream("no es una imagen"u8.ToArray()), $"{Path.GetFileName(brokenBase)}{TenantCfdiPdfLogos.Variant}",
            "image/webp", TenantStorageFolders.Logos(broken.Id));
        broken.Update(broken.Name, broken.ContactEmail, broken.ContactPhone, $"/uploads/{brokenBase}");
        await _context.SaveChangesAsync();

        Assert.Null(await _logos.PngAsync(gone.Id, CancellationToken.None));
        Assert.Null(await _logos.PngAsync(broken.Id, CancellationToken.None));
        Assert.Equal(new object[] { gone.Id, broken.Id }, _logger.Entries.Select(entry => entry["TenantId"]));
        Assert.All(_logger.Entries, entry => Assert.Equal(LogLevel.Warning, entry.Level));
    }

    [Fact]
    public async Task Cancellation_ShouldNotBeTakenAsALogoThatCannotBeRead()
    {
        var business = await BusinessAsync("Taquería");
        await GiveLogoAsync(business, 400, 200, Color.DarkRed);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _logos.PngAsync(business.Id, new CancellationToken(canceled: true)));
    }

    public void Dispose()
    {
        _context.Dispose();
        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
    }

    private async Task<Tenant> BusinessAsync(string name)
    {
        var tenant = new Tenant(name, $"negocio-{Guid.NewGuid():N}", BusinessType.Food, "dueno@example.com");
        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();
        return tenant;
    }

    // What Configuración › Mi negocio stores: the variants of the uploaded image in its folder, and their base URL.
    private async Task<string> GiveLogoAsync(Tenant tenant, int width, int height, Color color)
    {
        using var image = new Image<Rgba32>(width, height, color.ToPixel<Rgba32>());
        using var upload = new MemoryStream();
        await image.SaveAsPngAsync(upload);
        upload.Position = 0;

        var folder = TenantStorageFolders.Logos(tenant.Id);
        var baseId = Guid.NewGuid().ToString();
        foreach (var variant in await _images.GenerateVariantsAsync(upload))
        {
            using (variant)
                await _storage.UploadAsync(variant.Stream, $"{baseId}{variant.Suffix}.webp", "image/webp", folder);
        }

        var logoUrl = $"/uploads/{folder}/{baseId}";
        tenant.Update(tenant.Name, tenant.ContactEmail, tenant.ContactPhone, logoUrl);
        await _context.SaveChangesAsync();
        return logoUrl;
    }

    private static Image<Rgba32> AssertPng(byte[]? png)
    {
        Assert.NotNull(png);
        Assert.Equal(PngSignature, png[..PngSignature.Length]);
        return Image.Load<Rgba32>(png);
    }

    private sealed class WebHost(string contentRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "FoodParkDigital.Infrastructure.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = contentRootPath;
        public string EnvironmentName { get; set; } = "Testing";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(contentRootPath, "wwwroot");
    }
}
