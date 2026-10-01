namespace FoodParkDigital.Application.Common.Interfaces;

public interface IImageNormalizationService
{
    /// <summary>
    /// Decodes an uploaded image and re-encodes it as WebP, scaled down to <paramref name="maxDimension"/>
    /// and without metadata (EXIF location, camera data). Anything that is not a valid image, or declares
    /// more pixels than allowed, is rejected with a <c>DomainException</c>.
    /// </summary>
    Task<Stream> NormalizeToWebpAsync(Stream imageStream, int maxDimension, CancellationToken cancellationToken = default);
}
