using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Infrastructure.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>The PNG the PAC takes as the logo of a CFDI PDF (Fase 28L).</summary>
public class ImageProcessingServicePngTests
{
    private readonly ImageProcessingService _images = new();

    [Theory]
    [InlineData(1000, 100, 400, 40)]
    [InlineData(300, 600, 100, 200)]
    [InlineData(800, 400, 400, 200)]
    // Never enlarged: a small logo stays as it is.
    [InlineData(120, 60, 120, 60)]
    public async Task Png_ShouldFitTheBox_KeepingItsProportions(int width, int height, int expectedWidth, int expectedHeight)
    {
        var png = await _images.ToPngAsync(await WebpAsync(new Image<Rgba32>(width, height, Color.DarkRed.ToPixel<Rgba32>())), 400, 200);

        var info = Image.Identify(png);
        Assert.Equal("PNG", info.Metadata.DecodedImageFormat!.Name);
        Assert.Equal((expectedWidth, expectedHeight), (info.Width, info.Height));
    }

    [Fact]
    public async Task Png_ShouldCarryNoMetadata()
    {
        var image = new Image<Rgba32>(100, 50, Color.Transparent.ToPixel<Rgba32>());
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Artist, "Dueño del negocio");
        using var source = new MemoryStream();
        await image.SaveAsPngAsync(source);
        source.Position = 0;

        var png = await _images.ToPngAsync(source, 400, 200);

        using var result = Image.Load<Rgba32>(png);
        Assert.Null(result.Metadata.ExifProfile);
        Assert.Equal(0, result[50, 25].A);
    }

    [Fact]
    public async Task SomethingThatIsNotAnImage_ShouldBeRefused()
    {
        await Assert.ThrowsAsync<DomainException>(() => _images.ToPngAsync(new MemoryStream("no es una imagen"u8.ToArray()), 400, 200));
    }

    private static async Task<Stream> WebpAsync(Image<Rgba32> image)
    {
        using (image)
        {
            var stream = new MemoryStream();
            await image.SaveAsWebpAsync(stream);
            stream.Position = 0;
            return stream;
        }
    }
}
