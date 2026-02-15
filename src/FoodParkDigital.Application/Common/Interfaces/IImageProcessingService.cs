namespace FoodParkDigital.Application.Common.Interfaces;

public record ImageVariantResult(string Suffix, Stream Stream) : IDisposable
{
    public void Dispose() => Stream.Dispose();
}

public interface IImageProcessingService
{
    Task<IReadOnlyList<ImageVariantResult>> GenerateVariantsAsync(
        Stream originalStream,
        CancellationToken cancellationToken = default);
}
