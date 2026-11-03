using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Domain.Exceptions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FoodParkDigital.Infrastructure.Services;

public class ImageProcessingService : IImageProcessingService, IImageNormalizationService
{
    // A small file can declare a huge canvas (decompression bomb): the size is checked before decoding.
    private const long MaxPixels = 50_000_000;

    private static readonly (string Suffix, int MaxWidth)[] Variants =
    [
        ("_original", 2048),
        ("_lg", 1024),
        ("_md", 512),
        ("_sm", 256),
        ("_xs", 128)
    ];

    // Only the formats the upload endpoints accept, and only the first frame of an animation.
    private static readonly DecoderOptions DecoderOptions = new()
    {
        Configuration = new Configuration(
            new JpegConfigurationModule(),
            new PngConfigurationModule(),
            new WebpConfigurationModule(),
            new GifConfigurationModule()),
        MaxFrames = 1
    };

    // Uploaded photos can carry the GPS position and camera data: metadata is never re-encoded.
    private static readonly WebpEncoder WebpEncoder = new() { Quality = 80, SkipMetadata = true };
    private static readonly PngEncoder PngEncoder = new() { SkipMetadata = true };

    public async Task<IReadOnlyList<ImageVariantResult>> GenerateVariantsAsync(
        Stream originalStream,
        CancellationToken cancellationToken = default)
    {
        using var image = await LoadAsync(originalStream, cancellationToken);
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

    public async Task<Stream> NormalizeToWebpAsync(
        Stream imageStream,
        int maxDimension,
        CancellationToken cancellationToken = default)
    {
        // A larger JPEG, which is most phone photos, is decoded already within the size it is kept at; a PNG or a WebP decodes
        // whole and is then reduced (ImageSharp 3.1 does not scale them while reading), within the 50-megapixel cap. Every one
        // is decoded with 8 bits per channel, so a 16-bit PNG takes half (Fase 29H, also for photos sent without an account).
        using var image = await LoadAsync(imageStream, cancellationToken, decodeWithin: maxDimension, rgba32: true);
        image.Mutate(ctx =>
        {
            // The decoder leaves a side at the limit as it is, and the other may still be past it.
            var size = ctx.GetCurrentSize();
            if (size.Width > maxDimension || size.Height > maxDimension)
            {
                ctx.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxDimension, maxDimension)
                });
            }
        });

        var output = new MemoryStream();
        await image.SaveAsync(output, WebpEncoder, cancellationToken);
        output.Position = 0;
        return output;
    }

    /// <summary>
    /// The image as PNG within <paramref name="maxWidth"/> by <paramref name="maxHeight"/>, keeping its proportions and its
    /// transparency, without metadata: how the PAC takes a logo for the PDF of a CFDI (Fase 28L).
    /// </summary>
    public async Task<byte[]> ToPngAsync(Stream imageStream, int maxWidth, int maxHeight, CancellationToken cancellationToken = default)
    {
        using var image = await LoadAsync(imageStream, cancellationToken);
        image.Mutate(ctx =>
        {
            var size = ctx.GetCurrentSize();
            if (size.Width > maxWidth || size.Height > maxHeight)
                ctx.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(maxWidth, maxHeight) });
        });

        using var output = new MemoryStream();
        await image.SaveAsync(output, PngEncoder, cancellationToken);
        return output.ToArray();
    }

    /// <param name="decodeWithin">
    /// The longest side to decode an image past it within (<see cref="DecoderOptions.TargetSize"/>). An image within it is
    /// decoded as it is: the decoder fits any image to the target, enlarging a smaller one too.
    /// </param>
    private static async Task<Image> LoadAsync(
        Stream stream, CancellationToken cancellationToken, int? decodeWithin = null, bool rgba32 = false)
    {
        var seekable = stream;
        if (!stream.CanSeek)
        {
            seekable = new MemoryStream();
            await stream.CopyToAsync(seekable, cancellationToken);
        }

        try
        {
            seekable.Position = 0;
            var info = await Image.IdentifyAsync(DecoderOptions, seekable, cancellationToken);
            if ((long)info.Width * info.Height > MaxPixels)
                throw new DomainException("La imagen es demasiado grande (máximo 50 megapíxeles).");

            // A square box: the stored size is before the EXIF orientation, which turns the image after decoding.
            var options = decodeWithin is { } longest && (info.Width > longest || info.Height > longest)
                ? new DecoderOptions
                {
                    Configuration = DecoderOptions.Configuration,
                    MaxFrames = DecoderOptions.MaxFrames,
                    TargetSize = new Size(longest, longest)
                }
                : DecoderOptions;
            seekable.Position = 0;
            var image = rgba32
                ? await Image.LoadAsync<Rgba32>(options, seekable, cancellationToken)
                : await Image.LoadAsync(options, seekable, cancellationToken);
            // Applies the EXIF orientation to the pixels, since the metadata is dropped when encoding.
            image.Mutate(ctx => ctx.AutoOrient());
            return image;
        }
        catch (Exception ex) when (ex is ImageFormatException or NotSupportedException)
        {
            throw new DomainException("El archivo no es una imagen valida (JPG, PNG, WebP o GIF).");
        }
        finally
        {
            if (!ReferenceEquals(seekable, stream))
                await seekable.DisposeAsync();
        }
    }
}
