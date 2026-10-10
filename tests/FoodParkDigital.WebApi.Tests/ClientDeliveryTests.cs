using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// How the API hands the React client to the browser: the build's files in /assets carry a hash in their name, so
/// they are cached for a year and sent precompressed; index.html and every route of the app are checked again on
/// each visit, so a deploy shows at once. Nothing else is compressed.
/// </summary>
public sealed class ClientDeliveryTests : IClassFixture<ClientDeliveryTests.BuiltClient>
{
    private const string Immutable = "public, max-age=31536000, immutable";

    private readonly BuiltClient _client;

    public ClientDeliveryTests(BuiltClient client)
    {
        _client = client;
    }

    [Theory]
    [InlineData("br, gzip", "br")]
    [InlineData("gzip, deflate, br", "br")]
    [InlineData("gzip", "gzip")]
    [InlineData("br;q=0, gzip", "gzip")]
    public async Task AnAssetOfTheBuild_ShouldGoPrecompressedToABrowserThatAcceptsIt(string acceptEncoding, string expected)
    {
        using var response = await GetAsync("/assets/index-abc123.js", acceptEncoding);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([expected], response.Content.Headers.ContentEncoding);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Accept-Encoding", response.Headers.Vary);
        Assert.Equal(Immutable, response.Headers.CacheControl?.ToString());
        Assert.Equal(BuiltClient.Script, Decompress(expected, await response.Content.ReadAsByteArrayAsync()));
    }

    [Fact]
    public async Task AnAssetOfTheBuild_ShouldGoAsItIsToABrowserThatAcceptsNoCompression()
    {
        using var response = await GetAsync("/assets/index-abc123.js", acceptEncoding: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.Contains("Accept-Encoding", response.Headers.Vary);
        Assert.Equal(Immutable, response.Headers.CacheControl?.ToString());
        Assert.Equal(BuiltClient.Script, Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync()));
    }

    [Fact]
    public async Task AnAssetWithoutABrotliCopy_ShouldGoInTheNextEncodingTheBrowserAccepts()
    {
        using var css = await GetAsync("/assets/index-abc123.css", "br, gzip");
        Assert.Equal(["gzip"], css.Content.Headers.ContentEncoding);
        Assert.Equal("text/css", css.Content.Headers.ContentType?.MediaType);

        // A font is compressed already: it has no copies and goes as it is, still cached for a year.
        using var font = await GetAsync("/assets/remixicon-abc123.woff2", "br, gzip");
        Assert.Equal(HttpStatusCode.OK, font.StatusCode);
        Assert.Empty(font.Content.Headers.ContentEncoding);
        Assert.Equal(Immutable, font.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/m/dolce-piccante")]
    [InlineData("/orders/new")]
    [InlineData("/index.html")]
    public async Task ThePageOfTheApp_ShouldBeCheckedAgainOnEveryVisit(string path)
    {
        using var response = await GetAsync(path, "br, gzip");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
        Assert.Contains("/assets/index-abc123.js", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/assets/index-old999.js")]
    [InlineData("/assets/index-abc123.js.br")]
    [InlineData("/assets/index-abc123.js.gz")]
    public async Task AFileTheBuildDoesNotServe_ShouldNotBeFound(string path)
    {
        // An open page of an older version asks for a file the deploy replaced: a 404 (not the app's page) makes it
        // load the new version. The compressed copies only go out as the file they encode.
        using var response = await GetAsync(path, "br, gzip");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheApi_ShouldNotBeCompressed()
    {
        // Responses with a session's data are never compressed (no BREACH-style guessing through their size).
        using var response = await GetAsync("/swagger/v1/swagger.json", "br, gzip");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task AnUpload_ShouldKeepItsHeadersAndNoLongCache()
    {
        using var response = await GetAsync("/uploads/products/p1/photo_md.webp", "br, gzip");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("sandbox", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.NotEqual(Immutable, response.Headers.CacheControl?.ToString());
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string? acceptEncoding)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (acceptEncoding is not null)
            request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
        return await _client.Http.SendAsync(request);
    }

    private static string Decompress(string encoding, byte[] body)
    {
        using var input = new MemoryStream(body);
        using Stream decoder = encoding == "br"
            ? new BrotliStream(input, CompressionMode.Decompress)
            : new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(decoder, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>The API over a web root laid out as `npm run build` leaves it, without a database.</summary>
    public sealed class BuiltClient : IDisposable
    {
        public static readonly string Script = string.Concat(Enumerable.Repeat("console.log('TienditApp');\n", 200));

        private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "fpd-client-delivery", Guid.NewGuid().ToString("N"));
        private readonly WebApplicationFactory<Program> _factory;

        public BuiltClient()
        {
            var assets = Directory.CreateDirectory(Path.Combine(_webRoot, "assets")).FullName;
            File.WriteAllText(Path.Combine(_webRoot, "index.html"),
                "<!doctype html><html><head><script type=\"module\" src=\"/assets/index-abc123.js\"></script></head></html>");
            WriteWithCopies(Path.Combine(assets, "index-abc123.js"), Script, brotli: true);
            WriteWithCopies(Path.Combine(assets, "index-abc123.css"), string.Concat(Enumerable.Repeat(".card{padding:1.5rem}", 100)), brotli: false);
            File.WriteAllBytes(Path.Combine(assets, "remixicon-abc123.woff2"), [0x77, 0x4F, 0x46, 0x32, 1, 2, 3, 4]);
            var photo = Directory.CreateDirectory(Path.Combine(_webRoot, "uploads", "products", "p1")).FullName;
            File.WriteAllBytes(Path.Combine(photo, "photo_md.webp"), [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50]);

            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.UseEnvironment("Development");
                host.UseWebRoot(_webRoot);
                host.UseSetting("ConnectionStrings:DefaultConnection", "Server=127.0.0.1,1;Database=SinBase;Encrypt=False");
                host.UseSetting("Database:AutoMigrateOnStartup", "false");
                host.UseSetting("Database:SeedOnStartup", "false");
                host.UseSetting("BackgroundJobs:Enabled", "false");
            });
            Http = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        }

        public HttpClient Http { get; }

        public void Dispose()
        {
            Http.Dispose();
            _factory.Dispose();
            Directory.Delete(_webRoot, recursive: true);
        }

        private static void WriteWithCopies(string path, string content, bool brotli)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            File.WriteAllBytes(path, bytes);
            if (brotli)
                File.WriteAllBytes(path + ".br", Compress(bytes, output => new BrotliStream(output, CompressionLevel.SmallestSize)));
            File.WriteAllBytes(path + ".gz", Compress(bytes, output => new GZipStream(output, CompressionLevel.SmallestSize)));
        }

        private static byte[] Compress(byte[] bytes, Func<Stream, Stream> encoder)
        {
            using var output = new MemoryStream();
            using (var stream = encoder(output))
                stream.Write(bytes);
            return output.ToArray();
        }
    }
}
