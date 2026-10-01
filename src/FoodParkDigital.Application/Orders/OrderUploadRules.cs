using System.Text.RegularExpressions;

namespace FoodParkDigital.Application.Orders;

/// <summary>
/// Where order files are stored and which evidence URLs a payment may reference: only files uploaded
/// through the evidence endpoint for the same tenant (never an arbitrary link).
/// </summary>
public static class OrderUploadRules
{
    private const string FileNamePattern =
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\.(?:webp|pdf|jpg|jpeg|png)";

    public static string ServiceIntakeFolder(Guid tenantId, DateTimeOffset now) =>
        $"orders/{tenantId}/service-intake/{now:yyyyMM}";

    public static string PaymentEvidenceFolder(Guid tenantId, DateTimeOffset now) =>
        $"orders/{tenantId}/payment-evidence/{now:yyyyMM}";

    /// <summary>
    /// Accepts the URLs returned by the evidence upload for <paramref name="tenantId"/>: a local path
    /// ("/uploads/orders/{tenant}/payment-evidence/...") or an https storage URL with the same path.
    /// </summary>
    public static bool IsOwnPaymentEvidence(string? url, Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(url))
            return true;

        var value = url.Trim();
        string path;
        if (value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal))
            path = value;
        else if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            path = uri.AbsolutePath;
        else
            return false;

        // Segments cannot start with a dot, so relative paths cannot climb out of the tenant folder.
        var pattern = $@"^(?:/[A-Za-z0-9_-][A-Za-z0-9._-]*)*/orders/{tenantId}/payment-evidence/\d{{6}}/{FileNamePattern}$";
        return Regex.IsMatch(path, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

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
