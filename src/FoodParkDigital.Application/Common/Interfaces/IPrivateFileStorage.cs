namespace FoodParkDigital.Application.Common.Interfaces;

/// <summary>
/// Files only the business that uploaded them may open: transfer receipts and photos of what customers leave for
/// service. They are never served as static files or public storage URLs; the API streams them to members with the
/// right permission.
/// </summary>
public interface IPrivateFileStorage
{
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>The file's content, or null when there is no file with that key.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);
}
