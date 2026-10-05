using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FoodParkDigital.Application.Orders;

/// <summary>
/// Where order files are stored and which file references a payment or a service may keep: only files uploaded
/// through the app for the same business (never an arbitrary link). Transfer receipts and service photos are private:
/// their key is "orders/{tenantId}/{area}/{yyyyMM}/{guid}.{ext}" and the business keeps "/files/{key}", the API path
/// that opens them for its members.
/// </summary>
public static class OrderUploadRules
{
    public const string PaymentEvidenceArea = "payment-evidence";
    public const string ServiceIntakeArea = "service-intake";

    /// <summary>The path of a private file under the API (also what the records keep).</summary>
    public const string PrivateFilePathPrefix = "/files/";

    // Older uploads kept JPG or PNG; new ones are WebP, and receipts may also be PDF.
    private const string FileNamePattern =
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\.(?:webp|pdf|jpg|jpeg|png)";

    private static readonly Regex PrivateKeyPattern = new(
        $@"^orders/(?<tenant>[0-9a-fA-F]{{8}}-[0-9a-fA-F]{{4}}-[0-9a-fA-F]{{4}}-[0-9a-fA-F]{{4}}-[0-9a-fA-F]{{12}})/(?<area>{PaymentEvidenceArea}|{ServiceIntakeArea})/\d{{6}}/{FileNamePattern}$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static string ServiceIntakeFolder(Guid tenantId, DateTimeOffset now) =>
        $"orders/{tenantId}/{ServiceIntakeArea}/{now:yyyyMM}";

    public static string PaymentEvidenceFolder(Guid tenantId, DateTimeOffset now) =>
        $"orders/{tenantId}/{PaymentEvidenceArea}/{now:yyyyMM}";

    public static string ToPrivateFilePath(string key) => PrivateFilePathPrefix + key;

    /// <summary>The business and area of a private file key, when the key is one the app creates.</summary>
    public static bool TryParsePrivateKey(string? key, out Guid tenantId, out string area)
    {
        tenantId = Guid.Empty;
        area = string.Empty;
        if (string.IsNullOrEmpty(key))
            return false;

        var match = PrivateKeyPattern.Match(key);
        if (!match.Success)
            return false;

        tenantId = Guid.Parse(match.Groups["tenant"].Value);
        area = match.Groups["area"].Value;
        return true;
    }

    /// <summary>
    /// The key of a private file of <paramref name="tenantId"/> in <paramref name="area"/>, from any reference a record
    /// may hold: "/files/{key}", or the public address the file had before it became private (a local
    /// "/uploads/{key}" path or an https storage URL ending in the key).
    /// </summary>
    public static bool TryGetOwnFileKey(string? reference, Guid tenantId, string area, out string key)
    {
        key = string.Empty;
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        var value = reference.Trim();
        string path;
        if (value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal))
            path = value;
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            path = uri.AbsolutePath;
        else
            return false;

        // Segments cannot start with a dot, so a path cannot climb out of the business's folder.
        var pattern = $@"^(?:/[A-Za-z0-9_-][A-Za-z0-9._-]*)*/(?<key>orders/{tenantId}/{area}/\d{{6}}/{FileNamePattern})$";
        var match = Regex.Match(path, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success)
            return false;

        key = match.Groups["key"].Value;
        return true;
    }

    /// <summary>
    /// Accepts no receipt, or one uploaded through the app for <paramref name="tenantId"/>; <paramref name="path"/> is
    /// what the payment keeps: "/files/{key}", whichever address of the file it came with.
    /// </summary>
    public static bool TryGetPaymentEvidencePath(string? url, Guid tenantId, out string? path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(url))
            return true;
        if (!TryGetOwnFileKey(url, tenantId, PaymentEvidenceArea, out var key))
            return false;

        path = ToPrivateFilePath(key);
        return true;
    }

    public const int MaxServicePhotos = 20;
    public const string ServicePhotoError = "Cada foto del servicio debe subirse desde TienditApp para este negocio.";
    private const int MaxPhotoNameLength = 255;

    private static readonly JsonSerializerOptions PhotoJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// The photos of a service as the business keeps them (<c>[{"url":"/files/...","fileName":"..."}]</c>): only
    /// photos uploaded through the app for <paramref name="tenantId"/>, never a link to anything else.
    /// </summary>
    /// <returns>Why the photos are refused, or null.</returns>
    public static string? NormalizeServicePhotos(string? json, Guid tenantId, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(json))
            return null;

        JsonElement photos;
        try
        {
            using var document = JsonDocument.Parse(json);
            photos = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return ServicePhotoError;
        }

        if (photos.ValueKind != JsonValueKind.Array)
            return ServicePhotoError;
        if (photos.GetArrayLength() > MaxServicePhotos)
            return $"Un servicio admite hasta {MaxServicePhotos} fotos.";

        var kept = new List<ServicePhoto>();
        foreach (var photo in photos.EnumerateArray())
        {
            // The app sends { url, fileName }; older intakes kept the address alone.
            var url = photo.ValueKind switch
            {
                JsonValueKind.String => photo.GetString(),
                JsonValueKind.Object when photo.TryGetProperty("url", out var value) && value.ValueKind == JsonValueKind.String
                    => value.GetString(),
                _ => null
            };
            if (!TryGetOwnFileKey(url, tenantId, ServiceIntakeArea, out var key))
                return ServicePhotoError;

            string? fileName = null;
            if (photo.ValueKind == JsonValueKind.Object
                && photo.TryGetProperty("fileName", out var name)
                && name.ValueKind == JsonValueKind.String)
            {
                fileName = name.GetString()?.Trim();
                if (fileName?.Length > MaxPhotoNameLength)
                    fileName = fileName[..MaxPhotoNameLength];
            }

            kept.Add(new ServicePhoto(ToPrivateFilePath(key), string.IsNullOrEmpty(fileName) ? null : fileName));
        }

        normalized = kept.Count == 0 ? null : JsonSerializer.Serialize(kept, PhotoJsonOptions);
        return null;
    }

    private sealed record ServicePhoto(string Url, string? FileName);

    public static string ContentTypeOf(string key) => Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => "image/webp"
    };

    /// <summary>Returns a stream that can be read more than once (content sniffing, then decoding).</summary>
    public static async Task<Stream> BufferAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
            return stream;
        }

        var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return buffer;
    }
}
