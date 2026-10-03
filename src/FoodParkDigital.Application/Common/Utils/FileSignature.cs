namespace FoodParkDigital.Application.Common.Utils;

public enum UploadedFileKind
{
    Unknown = 0,
    Jpeg,
    Png,
    Webp,
    Pdf
}

/// <summary>
/// Detects the real type of an uploaded file from its first bytes ("magic numbers"). The file name and
/// the Content-Type header are chosen by the client and prove nothing about the content.
/// </summary>
public static class FileSignature
{
    private const int HeaderLength = 12;

    public static async Task<UploadedFileKind> DetectAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderLength];
        var read = 0;
        while (read < header.Length)
        {
            var count = await stream.ReadAsync(header.AsMemory(read), cancellationToken);
            if (count == 0)
                break;
            read += count;
        }

        return Detect(header.AsSpan(0, read));
    }

    public static UploadedFileKind Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return UploadedFileKind.Jpeg;

        if (header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return UploadedFileKind.Png;

        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return UploadedFileKind.Webp;

        if (header.StartsWith("%PDF-"u8))
            return UploadedFileKind.Pdf;

        return UploadedFileKind.Unknown;
    }

    public static bool IsImage(UploadedFileKind kind) =>
        kind is UploadedFileKind.Jpeg or UploadedFileKind.Png or UploadedFileKind.Webp;
}
