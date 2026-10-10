using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace FoodParkDigital.WebApi.Http;

/// <summary>
/// How the API hands the React client (client/dist, published into wwwroot) to the browser.
/// <list type="bullet">
/// <item>The build names every file in /assets after its content, so a new version never reuses a name: they are
/// cached for a year. When it publishes, the .NET SDK leaves a Brotli and a gzip copy next to each script and style
/// (static web assets compression, kept on in the csproj). The browser gets the copy it accepts, as the file it
/// encodes; nothing is compressed on each request.</item>
/// <item>index.html, which every route of the app serves, is checked again on each visit: a deploy shows at once.</item>
/// <item>Only /assets is compressed. The API's responses carry a session's data and are never compressed, so their
/// size cannot be used to guess it (BREACH).</item>
/// </list>
/// </summary>
public static class ClientDelivery
{
    public const string AssetsCacheControl = "public, max-age=31536000, immutable";
    public const string PageCacheControl = "no-cache";

    private static readonly PathString Assets = new("/assets");

    // In order of preference: Brotli is smaller.
    private static readonly (string Encoding, string Extension)[] Copies = [("br", ".br"), ("gzip", ".gz")];

    /// <summary>The build's files and the uploads, before authentication: none of them needs a session.</summary>
    public static IApplicationBuilder UseClientFiles(this IApplicationBuilder app)
    {
        app.Use(ServePrecompressedAsset);
        app.UseDefaultFiles();
        return app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = new CopyContentTypeProvider(),
            OnPrepareResponse = context =>
            {
                var request = context.Context.Request;
                var headers = context.Context.Response.Headers;
                if (request.Path.StartsWithSegments(Assets))
                {
                    headers.CacheControl = AssetsCacheControl;
                    return;
                }

                if (context.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                {
                    headers.CacheControl = PageCacheControl;
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
    /// Any other route that is not the API's is a screen of the app: it gets index.html, checked again on each visit.
    /// A missing file (a name with an extension) is a 404, so an open page of an older version reloads.
    /// </summary>
    public static void MapClientFallback(this WebApplication app)
    {
        app.MapFallback(async context =>
        {
            var path = context.Request.Path;
            if (path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var webRootPath = app.Environment.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRootPath))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var indexPath = Path.Combine(webRootPath, "index.html");
            if (!File.Exists(indexPath))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("Frontend artifact no encontrado. Asegura que client/dist se publique en wwwroot.");
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = PageCacheControl;
            await context.Response.SendFileAsync(indexPath);
        });
    }

    /// <summary>
    /// Points a request for a file in /assets at its compressed copy when the browser accepts it and the build left
    /// one. A request for a copy itself is not found: a copy only goes out as the file it encodes.
    /// </summary>
    private static Task ServePrecompressedAsset(HttpContext context, Func<Task> next)
    {
        var request = context.Request;
        if (!request.Path.StartsWithSegments(Assets) || !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)))
            return next();

        var path = request.Path.Value!;
        if (Copies.Any(copy => path.EndsWith(copy.Extension, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        // The same address answers differently by Accept-Encoding: caches must keep them apart.
        context.Response.Headers.Append(HeaderNames.Vary, HeaderNames.AcceptEncoding);
        var files = context.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider;
        foreach (var (encoding, extension) in Copies)
        {
            if (!Accepts(request, encoding) || !files.GetFileInfo(path + extension).Exists)
                continue;

            request.Path = new PathString(path + extension);
            context.Response.Headers.ContentEncoding = encoding;
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
