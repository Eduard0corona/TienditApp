using System.Text.RegularExpressions;
using FoodParkDigital.Domain.WhatsApp.Enums;

namespace FoodParkDigital.Application.WhatsApp.Media;

/// <summary>
/// Where the photos and audios customers send by WhatsApp are kept (Fase 31L, ADR 0092), how big they may be and which audio
/// is kept: privately, at "whatsapp/{tenantId}/{yyyyMM}/{guid}.{ext}", which the message keeps as "/files/{key}", the API path
/// that opens it for whoever runs the chat. What a file is comes from its content, never from what WhatsApp says.
/// </summary>
public static partial class WhatsAppMediaRules
{
    public const string Area = "whatsapp";

    /// <summary>The largest photo kept: WhatsApp takes photos up to 5 MB.</summary>
    public const long MaxImageBytes = 5 * 1024 * 1024;

    /// <summary>The largest audio kept: WhatsApp takes audios up to 16 MB.</summary>
    public const long MaxAudioBytes = 16 * 1024 * 1024;

    /// <summary>The longest side of a photo once kept, like the photos of a service.</summary>
    public const int MaxImageDimension = 1600;

    public static long MaxBytes(WhatsAppMediaKind kind) => kind == WhatsAppMediaKind.Image ? MaxImageBytes : MaxAudioBytes;

    public static string Folder(Guid tenantId, DateTimeOffset now) => $"{Area}/{tenantId}/{now:yyyyMM}";

    /// <summary>The business of a key the app creates for a photo or audio of WhatsApp.</summary>
    public static bool TryParseKey(string? key, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (string.IsNullOrEmpty(key))
            return false;

        var match = KeyPattern().Match(key);
        if (!match.Success)
            return false;

        tenantId = Guid.Parse(match.Groups["tenant"].Value);
        return true;
    }

    /// <summary>What a kept file is, by its extension: the app wrote it so.</summary>
    public static string ContentTypeOf(string key) => Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".webp" => "image/webp",
        ".ogg" => "audio/ogg",
        ".mp3" => "audio/mpeg",
        ".m4a" => "audio/mp4",
        ".aac" => "audio/aac",
        _ => "application/octet-stream"
    };

    /// <summary>
    /// The extension and type of an audio a browser plays, from its first bytes: Ogg (WhatsApp's voice notes), MP3, MP4/M4A or
    /// AAC. Null for anything else, AMR included (no browser plays it).
    /// </summary>
    public static (string Extension, string ContentType)? DetectAudio(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith("OggS"u8))
            return (".ogg", "audio/ogg");
        if (header.StartsWith("ID3"u8) || (header.Length >= 2 && header[0] == 0xFF && header[1] is 0xFB or 0xF3 or 0xF2))
            return (".mp3", "audio/mpeg");
        if (header.Length >= 8 && header[4..8].SequenceEqual("ftyp"u8))
            return (".m4a", "audio/mp4");
        if (header.Length >= 2 && header[0] == 0xFF && header[1] is 0xF1 or 0xF9)
            return (".aac", "audio/aac");
        return null;
    }

    [GeneratedRegex(
        @"^whatsapp/(?<tenant>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})/\d{6}/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\.(?:webp|ogg|mp3|m4a|aac)$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex KeyPattern();
}
