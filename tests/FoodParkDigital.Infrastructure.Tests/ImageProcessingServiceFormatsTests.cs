using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Infrastructure.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// The service decodes only JPEG, PNG, WebP and GIF. ImageSharp 3.1.12 stays while its TIFF advisories cannot be reached
/// (GHSA-wmxv-xphr-5c9g, GHSA-j3p4-wp97-rph4, GHSA-j9gm-c75j-xc9q, GHSA-jjfr-hcj7-qf5w, GHSA-v76p-62qx-wwq2; see
/// docs/roadmap/technical-debt.md): product photos, covers, avatars and logos reach it without a check of their first
/// bytes, so the formats it registers are what keeps a TIFF from being decoded.
/// </summary>
public class ImageProcessingServiceFormatsTests
{
    private const string NotAnImage = "no es una imagen";

    private readonly ImageProcessingService _images = new();

    [Fact]
    public async Task Tiff_ShouldBeRefusedBeforeDecoding_ByEveryEntryPoint()
    {
        var tiff = Tiff();
        // ImageSharp's default configuration reads it: the refusal comes from the formats the service registers.
        Assert.Equal("TIFF", Image.Identify(tiff).Metadata.DecodedImageFormat!.Name);

        var variants = await Assert.ThrowsAsync<DomainException>(() => _images.GenerateVariantsAsync(new MemoryStream(tiff)));
        var webp = await Assert.ThrowsAsync<DomainException>(() => _images.NormalizeToWebpAsync(new MemoryStream(tiff), 2048));
        var png = await Assert.ThrowsAsync<DomainException>(() => _images.ToPngAsync(new MemoryStream(tiff), 400, 200));

        Assert.All([variants, webp, png], exception => Assert.Contains(NotAnImage, exception.Message));
    }

    private static byte[] Tiff()
    {
        using var image = new Image<Rgba32>(16, 16, Color.DarkRed.ToPixel<Rgba32>());
        using var stream = new MemoryStream();
        image.SaveAsTiff(stream);
        return stream.ToArray();
    }
}
