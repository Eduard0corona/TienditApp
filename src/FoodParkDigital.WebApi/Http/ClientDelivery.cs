using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace FoodParkDigital.WebApi.Http;

/// <summary>
/// How the API hands the React client (client/dist, published into wwwroot) to the browser.
/// <list type="bullet">
/// <item>The build names every file in /assets after its content, so a new version never reuses a name: they are
/// cached for a year. When it publishes, the .NET SDK leaves a Brotli and a gzip copy next to each file that
/// compresses (static web assets compression, kept on in the csproj): scripts and styles, and also index.html and
/// the images. The browser gets the copy of a file in /assets it accepts, as the file it encodes; nothing is
/// compressed on each request, and no copy is served by its own name.</item>
/// <item>index.html, which every route of the app serves, is checked again on each visit: a deploy shows at once. The
/// public pages get it with their own head (<see cref="PublicPages"/>); every other page stays out of search engines.</item>
/// <item>Only /assets is compressed. The API's responses carry a session's data and are never compressed, so their
/// size cannot be used to guess it (BREACH).</item>
/// </list>
/// </summary>
public static class ClientDelivery
{
    public const string AssetsCacheControl = "public, max-age=31536000, immutable";
    public const string PageCacheControl = "no-cache";

    private static readonly PathString Assets = new("/assets");

    // The encoding of the copy a request in /assets was pointed at, written as a header only if the file goes out.
    private const string CopyEncodingKey = "ClientDelivery.CopyEncoding";

    // In order of preference: Brotli is smaller.
    private static readonly (string Encoding, string Extension)[] Copies = [("br", ".br"), ("gzip", ".gz")];

    /// <summary>The build's files and the uploads, before authentication: none of them needs a session.</summary>
    public static IApplicationBuilder UseClientFiles(this IApplicationBuilder app)
    {
        app.Use(ServePrecompressedAsset);
        // No default file: / is a public page and gets its head from the fallback, like any other page of the app.
        return app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = new CopyContentTypeProvider(),
            OnPrepareResponse = context =>
            {
                var request = context.Context.Request;
                var response = context.Context.Response;
                var headers = response.Headers;
                if (request.Path.StartsWithSegments(Assets))
                {
                    // Only a file that goes out (200, 206) or is confirmed (304) is encoded and kept a year. A failed
                    // precondition or range (412, 416) kept by a shared cache would stand in for the script.
                    if (response.StatusCode >= StatusCodes.Status400BadRequest)
                        return;

                    headers.CacheControl = AssetsCacheControl;
                    if (context.Context.Items[CopyEncodingKey] is string encoding)
                        headers.ContentEncoding = encoding;
                    return;
                }

                if (context.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                {
                    // /index.html by its name is the app without a page: / is the one search engines list.
                    headers.CacheControl = PageCacheControl;
                    headers[PublicPages.RobotsHeader] = "noindex";
                    return;
                }

                // User uploads are served from the API origin: they must never run as active content.
                if (!request.Path.StartsWithSegments("/uploads"))
                    return;

                headers.ContentSecurityPolicy = "sandbox";
                if (context.File.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    headers.ContentDisposition = "attachment";
            },
        });
    }

    /// <summary>
    /// Any other route that is not the API's is a screen of the app: it gets index.html, checked again on each visit,
    /// with the head of Explorar and its categories; the store pages have their own endpoints (<see cref="PublicPages"/>).
    /// A missing file (a name with an extension) is a 404, so an open page of an older version reloads.
    /// </summary>
    public static void MapClientFallback(this WebApplication app)
    {
        app.MapFallback(context =>
        {
            var path = context.Request.Path;
            if (path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }

            return PublicPages.WriteIndexAsync(context, app.Environment, PublicPages.StaticHeadFor(path));
        });
    }

    /// <summary>
    /// Points a request for a file in /assets at its compressed copy when the browser accepts it and the build left
    /// one. A request for a copy itself is not found, in /assets or anywhere else the publish left them (index.html,
    /// the images): a copy only goes out as the file it encodes.
    /// </summary>
    private static Task ServePrecompressedAsset(HttpContext context, Func<Task> next)
    {
        var request = context.Request;
        if (!(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            || request.Path.StartsWithSegments("/api") || request.Path.StartsWithSegments("/hubs"))
            return next();

        var path = request.Path.Value ?? string.Empty;
        if (Copies.Any(copy => path.EndsWith(copy.Extension, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        if (!request.Path.StartsWithSegments(Assets))
            return next();

        // The same address answers differently by Accept-Encoding: caches must keep them apart.
        context.Response.Headers.Append(HeaderNames.Vary, HeaderNames.AcceptEncoding);
        var files = context.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider;
        foreach (var (encoding, extension) in Copies)
        {
            if (!Accepts(request, encoding) || !files.GetFileInfo(path + extension).Exists)
                continue;

            request.Path = new PathString(path + extension);
            context.Items[CopyEncodingKey] = encoding;
            break;
        }

        return next();
    }

    /// <summary>Whether Accept-Encoding takes it, by name or by "*", with a quality above zero.</summary>
    private static bool Accepts(HttpRequest request, string encoding)
    {
        var accepted = request.GetTypedHeaders().AcceptEncoding;
        var match = accepted.FirstOrDefault(value => value.Value.Equals(encoding, StringComparison.OrdinalIgnoreCase))
            ?? accepted.FirstOrDefault(value => value.Value.Equals("*", StringComparison.Ordinal));
        return match is not null && (match.Quality ?? 1) > 0;
    }

    /// <summary>A compressed copy in /assets has the type of the file it encodes ("index-abc.js.br" is a script).</summary>
    private sealed class CopyContentTypeProvider : IContentTypeProvider
    {
        private readonly FileExtensionContentTypeProvider _types = new();

        public bool TryGetContentType(string subpath, out string contentType)
        {
            var copy = subpath.StartsWith(Assets + "/", StringComparison.OrdinalIgnoreCase)
                ? Copies.FirstOrDefault(c => subpath.EndsWith(c.Extension, StringComparison.OrdinalIgnoreCase))
                : default;
            var file = copy.Extension is null ? subpath : subpath[..^copy.Extension.Length];
            return _types.TryGetContentType(file, out contentType!);
        }
    }
}
