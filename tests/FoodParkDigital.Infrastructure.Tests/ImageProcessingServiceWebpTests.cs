using FoodParkDigital.Infrastructure.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace FoodParkDigital.Infrastructure.Tests;

/// <summary>
/// A photo as it is kept: the photos of what happens of a visit (Fase 29H, ADR 0062), the photos of a service and the
/// transfer receipts. WebP within the longest side, upright, without metadata, and never enlarged.
/// </summary>
public class ImageProcessingServiceWebpTests
{
    private const int MaxDimension = 2048;

    private readonly ImageProcessingService _images = new();

    [Theory]
    [InlineData("jpeg", 3000, 1000, 2048, 683)]
    [InlineData("png", 1000, 3000, 683, 2048)]
    [InlineData("jpeg", 4096, 3072, 2048, 1536)]
    [InlineData("webp", 2600, 2600, 2048, 2048)]
    // One side already at the limit and the other past it.
    [InlineData("jpeg", 4000, 2048, 2048, 1049)]
    [InlineData("png", 2048, 4000, 1049, 2048)]
    // Never enlarged: a small photo, or a screenshot of a receipt, stays as it is.
    [InlineData("jpeg", 640, 480, 640, 480)]
    [InlineData("png", 640, 480, 640, 480)]
    [InlineData("webp", 300, 900, 300, 900)]
    [InlineData("jpeg", 2048, 1536, 2048, 1536)]
    [InlineData("png", 2048, 2048, 2048, 2048)]
    public async Task Photo_ShouldFitTheLongestSide_NeverEnlarged(
        string format, int width, int height, int expectedWidth, int expectedHeight)
    {
        await using var source = await EncodeAsync(new Image<Rgba32>(width, height, Color.SteelBlue.ToPixel<Rgba32>()), format);

        await using var kept = await _images.NormalizeToWebpAsync(source, MaxDimension);

        var info = await Image.IdentifyAsync(kept);
        Assert.Equal("Webp", info.Metadata.DecodedImageFormat!.Name);
        Assert.Equal((expectedWidth, expectedHeight), (info.Width, info.Height));
    }

    [Theory]
    // Taken sideways: the pixels lie down and the camera says to turn them.
    [InlineData(3000, 1000, 683, 2048)]
    [InlineData(640, 480, 480, 640)]
    public async Task Photo_ShouldStandAsTheCameraTookIt_WithoutItsMetadata(
        int width, int height, int expectedWidth, int expectedHeight)
    {
        var image = new Image<Rgba32>(width, height, Color.SteelBlue.ToPixel<Rgba32>());
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
        image.Metadata.ExifProfile.SetValue(ExifTag.Model, "Teléfono del cliente");
        await using var source = await EncodeAsync(image, "jpeg");

        await using var kept = await _images.NormalizeToWebpAsync(source, MaxDimension);

        var info = await Image.IdentifyAsync(kept);
        Assert.Equal((expectedWidth, expectedHeight), (info.Width, info.Height));
        Assert.Null(info.Metadata.ExifProfile);
    }

    private static async Task<Stream> EncodeAsync(Image<Rgba32> image, string format)
    {
        using (image)
        {
            var stream = new MemoryStream();
            switch (format)
            {
                case "jpeg":
                    await image.SaveAsJpegAsync(stream);
                    break;
                case "png":
                    await image.SaveAsPngAsync(stream);
                    break;
                default:
                    await image.SaveAsWebpAsync(stream);
                    break;
            }

            stream.Position = 0;
            return stream;
        }
    }
}
