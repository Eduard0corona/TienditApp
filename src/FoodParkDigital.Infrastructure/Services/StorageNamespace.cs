using System.Buffers;

namespace FoodParkDigital.Infrastructure.Services;

// Path rules shared by the storage providers: a stored URL may only reach files inside the folder the
// caller derives from the owning tenant (for example "products/{tenantId}").
internal static class StorageNamespace
{
    // The upload pipeline only produces these characters (fixed folders, GUIDs, variant suffixes and
    // extensions). Rejecting anything else rules out encoded separators, '\', ':' and other
    // platform-specific path tricks before a file path or blob name is built.
    private static readonly SearchValues<char> AllowedChars =
        SearchValues.Create("-./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz");

    public static void EnsureValidFolder(string folder)
    {
        if (!IsCanonical(folder))
            throw new ArgumentException("La carpeta de almacenamiento no es válida.", nameof(folder));
    }

    // A private file key ("orders/{tenant}/..."): canonical, with a folder and a file name. Traversal is rejected.
    public static bool IsValidKey(string? key) => IsCanonical(key) && key!.Contains('/');

    // True when relativePath is canonical and strictly below folder. Traversal is rejected, not normalized.
    public static bool IsInsideFolder(string relativePath, string folder) =>
        IsCanonical(relativePath)
        && relativePath.Length > folder.Length
        && relativePath.StartsWith(folder, StringComparison.Ordinal)
        && relativePath[folder.Length] == '/';

    // A "/"-separated relative path whose segments are non-empty and neither start nor end with '.',
    // which excludes "." and ".." as well as the trailing-dot aliases Windows strips from names.
    private static bool IsCanonical(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.AsSpan().ContainsAnyExcept(AllowedChars))
            return false;

        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment[0] == '.' || segment[^1] == '.')
                return false;
        }

        return true;
    }
}
