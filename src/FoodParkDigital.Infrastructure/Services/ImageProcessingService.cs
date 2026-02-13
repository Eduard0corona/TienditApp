using FoodParkDigital.Application.Common.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace FoodParkDigital.Infrastructure.Services;

public class ImageProcessingService : IImageProcessingService
{
    private static readonly (string Suffix, int MaxWidth)[] Variants =
    [
        ("_original", 2048),
        ("_lg", 1024),
        ("_md", 512),
        ("_sm", 256),
        ("_xs", 128)
    ];

    private static readonly WebpEncoder WebpEncoder = new() { Quality = 80 };

    public async Task<IReadOnlyList<ImageVariantResult>> GenerateVariantsAsync(
        Stream originalStream,
        CancellationToken cancellationToken = default)
    {
        using var image = await Image.LoadAsync(originalStream, cancellationToken);
        var results = new List<ImageVariantResult>(Variants.Length);

        foreach (var (suffix, maxWidth) in Variants)
        {
            var variant = image.Clone(ctx =>
            {
                if (ctx.GetCurrentSize().Width > maxWidth)
                {
                    ctx.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(maxWidth, 0)
                    });
                }
            });

            var ms = new MemoryStream();
            await variant.SaveAsync(ms, WebpEncoder, cancellationToken);
            ms.Position = 0;
            variant.Dispose();

            results.Add(new ImageVariantResult(suffix, ms));
        }

        return results;
    }
}
