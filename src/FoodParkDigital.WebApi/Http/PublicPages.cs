using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Text.RegularExpressions;
using System.Xml;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Domain.Tenants.Enums;
using MediatR;

namespace FoodParkDigital.WebApi.Http;

/// <summary>
/// The public pages as search engines and the previews of a shared link read them (Fase 24D-2, ADR 0027). The app draws
/// every page in the browser, so its index.html says nothing about a store: for a store (/m/{slug}), its booking page and
/// Explorar, the server writes into it the title, the description, the image and the address of the page, and for a
/// store its structured data. Everything else of the app stays out of search engines. robots.txt and sitemap.xml say
/// what to read.
/// </summary>
public static class PublicPages
{
    public const string SiteName = "TienditApp";

    /// <summary>What search engines show of a description; a longer one is cut with an ellipsis.</summary>
    public const int MaxDescriptionLength = 160;

    /// <summary>A slug longer than a business can have is no store: answered without asking the database.</summary>
    public const int MaxSlugLength = 100;

    /// <summary>The header that keeps a page out of search engines, also one they read without its HTML.</summary>
    public const string RobotsHeader = "X-Robots-Tag";

    private const string NoIndex = "noindex";
    private const string ExploreDescription =
        "Compra en los negocios de tu ciudad: mira su menú, pide en línea o agenda tu cita.";

    private static readonly Regex TitleTag = new("<title>.*?</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-MX");

    // The default encoder escapes <, >, & and quotes: nothing a business writes can close the script it goes in.
    private static readonly JsonSerializerOptions StructuredData = new() { WriteIndented = false };

    /// <summary>The store and booking pages (they read the store), robots.txt and sitemap.xml.</summary>
    public static void MapPublicPages(this WebApplication app)
    {
        string[] read = [HttpMethods.Get, HttpMethods.Head];
        // Pages, not API: none of them goes in the contract.
        app.MapMethods("/robots.txt", read, RobotsAsync).ExcludeFromDescription();
        // These read the database: they share the limit of the public menu.
        app.MapMethods("/sitemap.xml", read, SitemapAsync).RequireRateLimiting("public-menu").ExcludeFromDescription();
        app.MapMethods("/m/{slug}", read, (HttpContext context, string slug) => StorePageAsync(context, slug, booking: false))
            .RequireRateLimiting("public-menu").ExcludeFromDescription();
        app.MapMethods("/m/{slug}/agendar", read, (HttpContext context, string slug) => StorePageAsync(context, slug, booking: true))
            .RequireRateLimiting("public-menu").ExcludeFromDescription();
    }

    /// <summary>
    /// The head of a page of the app that needs nothing from the database: Explorar and its categories (and / for
    /// visitors, which opens Explorar). Null for every other page: it stays out of search engines.
    /// </summary>
    public static PageHead? StaticHeadFor(PathString path)
    {
        var value = (path.Value ?? "/").TrimEnd('/');
        if (value.Length == 0 || value.Equals("/explorar", StringComparison.OrdinalIgnoreCase))
            return new PageHead($"Explorar · {SiteName}", ExploreDescription, "/explorar", null, false, true, null);

        const string prefix = "/explorar/";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || value.IndexOf('/', prefix.Length) >= 0)
            return null;

        var category = ExploreCategories.BySlug(value[prefix.Length..]);
        if (category is null)
            // The page shows every category: the same page as Explorar, kept out of search engines.
            return new PageHead($"Explorar · {SiteName}", ExploreDescription, "/explorar", null, false, false, null);
        return new PageHead(
            $"{category.Label} · Explorar · {SiteName}",
            $"Negocios de {category.Label.ToLower(Spanish)} en {SiteName}: mira su menú, pide en línea o agenda tu cita.",
            $"/explorar/{category.Slug}",
            null, false, true, null);
    }

    /// <summary>The head of a store or its booking page, from what its menu already shows.</summary>
    public static PageHead StoreHead(StorePreviewDto store, bool booking)
    {
        var path = $"/m/{Uri.EscapeDataString(store.Slug)}";
        var image = store.CoverImageUrl ?? store.LogoUrl;
        var place = store.Address?.City is { } city ? $" En {city}." : string.Empty;
        if (booking)
            return new PageHead(
                $"Agenda tu cita en {store.BusinessName} · {SiteName}",
                $"Elige el servicio, el día y la hora, y agenda tu cita en {store.BusinessName}.{place}",
                $"{path}/agendar",
                image,
                store.CoverImageUrl is not null,
                // A store that does not book online shows it on that page: nothing to list.
                store.ListedInDirectory && store.BooksOnline,
                null);

        var (title, fallback) = store switch
        {
            { TakesOrders: true } => ("pide en línea", $"Mira el menú de {store.BusinessName} y haz tu pedido en línea."),
            { BooksOnline: true } => ("agenda tu cita", $"Mira los servicios de {store.BusinessName} y agenda tu cita en línea."),
            _ => ("menú en línea", $"Mira el menú de {store.BusinessName} en línea.")
        };
        var description = string.IsNullOrWhiteSpace(store.Description) ? fallback : store.Description.Trim().TrimEnd('.') + ".";
        return new PageHead(
            $"{store.BusinessName}: {title} · {SiteName}",
            description + place,
            path,
            image,
            store.CoverImageUrl is not null,
            store.ListedInDirectory,
            store);
    }

    /// <summary>
    /// index.html with the head of the page: its title replaced and the tags before &lt;/head&gt;. Without a head the
    /// page is the app's own, out of search engines. Checked again on every visit, like every page of the app.
    /// </summary>
    public static async Task WriteIndexAsync(HttpContext context, IWebHostEnvironment environment, PageHead? head, int statusCode = StatusCodes.Status200OK)
    {
        var webRootPath = environment.WebRootPath;
        var indexPath = string.IsNullOrWhiteSpace(webRootPath) ? null : Path.Combine(webRootPath, "index.html");
        if (indexPath is null || !File.Exists(indexPath))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            if (indexPath is not null)
                await context.Response.WriteAsync("Frontend artifact no encontrado. Asegura que client/dist se publique en wwwroot.");
            return;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = ClientDelivery.PageCacheControl;
        if (head is null)
        {
            // An outage (503) leaves search engines to come back; any other page of the app stays out of them.
            if (statusCode != StatusCodes.Status503ServiceUnavailable)
                context.Response.Headers[RobotsHeader] = NoIndex;
            await context.Response.SendFileAsync(indexPath, context.RequestAborted);
            return;
        }

        if (!head.Indexable)
            context.Response.Headers[RobotsHeader] = NoIndex;
        var html = await File.ReadAllTextAsync(indexPath, context.RequestAborted);
        await context.Response.WriteAsync(WithHead(html, head.Render(BaseUrl(context))), context.RequestAborted);
    }

    /// <summary>The page with its own title and tags; as it is when it has no &lt;/head&gt;.</summary>
    public static string WithHead(string html, string tags)
    {
        var withoutTitle = TitleTag.Replace(html, string.Empty, 1);
        var close = withoutTitle.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return close < 0 ? html : withoutTitle.Insert(close, tags);
    }

    /// <summary>
    /// The address of the app for links that leave it (canonical, preview, sitemap): the configured AppUrl, or else the
    /// address this request came to, always https outside Development.
    /// </summary>
    public static string BaseUrl(HttpContext context)
    {
        var configured = context.RequestServices.GetService<IAppLinks>()?.BaseUrl;
        if (!string.IsNullOrEmpty(configured))
            return configured.TrimEnd('/');
        var development = context.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();
        var scheme = development ? context.Request.Scheme : Uri.UriSchemeHttps;
        return $"{scheme}://{context.Request.Host.Value}";
    }

    private static async Task StorePageAsync(HttpContext context, string slug, bool booking)
    {
        var environment = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
        if (slug.Length > MaxSlugLength)
        {
            await WriteIndexAsync(context, environment, NotFoundHead, StatusCodes.Status404NotFound);
            return;
        }

        StorePreviewDto? store;
        try
        {
            store = await context.RequestServices.GetRequiredService<ISender>().Send(new GetStorePreviewQuery(slug), context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The page still opens; search engines are asked to come back instead of reading a store without its name.
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PublicPages))
                .LogWarning(exception, "The preview of a store could not be read; its page goes without it.");
            context.Response.Headers.RetryAfter = "120";
            await WriteIndexAsync(context, environment, null, StatusCodes.Status503ServiceUnavailable);
            return;
        }

        // An unknown slug or a menu that is not published: its page says so, and it is not a page to list.
        if (store is null)
            await WriteIndexAsync(context, environment, NotFoundHead, StatusCodes.Status404NotFound);
        else
            await WriteIndexAsync(context, environment, StoreHead(store, booking));
    }

    private static readonly PageHead NotFoundHead =
        new($"Menú no encontrado · {SiteName}", "Este menú no existe o el negocio no lo ha publicado.", null, null, false, false, null);

    private static async Task RobotsAsync(HttpContext context)
    {
        // The app reads the public API to draw the public pages: search engines need it too. The rest of the API, no.
        var robots = new StringBuilder()
            .Append("User-agent: *\n")
            .Append("Allow: /api/v1/public/\n")
            .Append("Disallow: /api/\n")
            .Append("Disallow: /hubs/\n")
            .Append('\n')
            .Append("Sitemap: ").Append(BaseUrl(context)).Append("/sitemap.xml\n");
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.Headers.CacheControl = "public, max-age=3600";
        await context.Response.WriteAsync(robots.ToString(), context.RequestAborted);
    }

    private static async Task SitemapAsync(HttpContext context)
    {
        var sitemap = await context.RequestServices.GetRequiredService<ISender>().Send(new GetPublicSitemapQuery(), context.RequestAborted);
        var baseUrl = BaseUrl(context);
        var paths = new List<string> { "/explorar" };
        paths.AddRange(sitemap.Categories.Select(ExploreCategories.Of).Where(category => category is not null).Select(category => $"/explorar/{category!.Slug}"));
        foreach (var store in sitemap.Stores)
        {
            var path = $"/m/{Uri.EscapeDataString(store.Slug)}";
            paths.Add(path);
            if (store.BooksOnline)
                paths.Add($"{path}/agendar");
        }

        using var buffer = new MemoryStream();
        await using (var xml = XmlWriter.Create(buffer, new XmlWriterSettings { Async = true, Encoding = new UTF8Encoding(false) }))
        {
            await xml.WriteStartDocumentAsync();
            await xml.WriteStartElementAsync(null, "urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
            foreach (var path in paths)
            {
                await xml.WriteStartElementAsync(null, "url", null);
                await xml.WriteElementStringAsync(null, "loc", null, baseUrl + path);
                await xml.WriteEndElementAsync();
            }

            await xml.WriteEndElementAsync();
            await xml.WriteEndDocumentAsync();
        }

        context.Response.ContentType = "application/xml; charset=utf-8";
        context.Response.Headers.CacheControl = "public, max-age=3600";
        await context.Response.Body.WriteAsync(buffer.ToArray(), context.RequestAborted);
    }

    /// <summary>
    /// The head of a public page. <paramref name="CanonicalPath"/> is its address without the query (a table's QR adds
    /// one); <paramref name="LargeImage"/> says the image is a cover, wide, rather than a logo.
    /// </summary>
    public sealed record PageHead(
        string Title,
        string Description,
        string? CanonicalPath,
        string? ImageUrl,
        bool LargeImage,
        bool Indexable,
        StorePreviewDto? Store)
    {
        /// <summary>The tags, every value escaped: names and descriptions are written by the businesses.</summary>
        public string Render(string baseUrl)
        {
            var description = Shorten(Description);
            var url = CanonicalPath is null ? null : baseUrl + CanonicalPath;
            var image = ImageUrl is null ? null : Absolute(baseUrl, ImageUrl);
            var tags = new StringBuilder()
                .Append("<title>").Append(Html(Title)).Append("</title>\n")
                .Append(Meta("name", "description", description))
                .Append(Meta("name", "robots", Indexable ? "index, follow" : NoIndex));
            if (url is not null && Indexable)
                tags.Append("<link rel=\"canonical\" href=\"").Append(Html(url)).Append("\">\n");
            tags.Append(Meta("property", "og:site_name", SiteName))
                .Append(Meta("property", "og:locale", "es_MX"))
                .Append(Meta("property", "og:type", "website"))
                .Append(Meta("property", "og:title", Title))
                .Append(Meta("property", "og:description", description));
            if (url is not null)
                tags.Append(Meta("property", "og:url", url));
            if (image is not null)
                tags.Append(Meta("property", "og:image", image));
            tags.Append(Meta("name", "twitter:card", image is not null && LargeImage ? "summary_large_image" : "summary"));
            if (Store is not null && url is not null)
                tags.Append("<script type=\"application/ld+json\">")
                    .Append(JsonSerializer.Serialize(StructuredDataOf(Store, url, description, baseUrl), StructuredData))
                    .Append("</script>\n");
            return tags.ToString();
        }

        private static string Meta(string attribute, string name, string content)
            => $"<meta {attribute}=\"{name}\" content=\"{Html(content)}\">\n";
    }

    /// <summary>The store as schema.org reads a local business: only what its menu already shows.</summary>
    private static Dictionary<string, object?> StructuredDataOf(StorePreviewDto store, string url, string description, string baseUrl)
    {
        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = SchemaTypeOf(store.Category),
            ["name"] = store.BusinessName,
            ["url"] = url,
            ["description"] = description
        };
        var images = new[] { store.CoverImageUrl, store.LogoUrl }
            .Where(image => image is not null)
            .Select(image => Absolute(baseUrl, image!))
            .ToList();
        if (images.Count > 0)
            data["image"] = images;
        if (store.Category == BusinessCategory.Food)
            data["hasMenu"] = url;
        if (store.ContactPhone is not null)
            data["telephone"] = store.ContactPhone;
        if (store.Address is { } address)
        {
            var postal = new Dictionary<string, object?>
            {
                ["@type"] = "PostalAddress",
                ["streetAddress"] = address.Street,
                ["addressCountry"] = address.Country ?? "MX"
            };
            if (address.City is not null)
                postal["addressLocality"] = address.City;
            if (address.State is not null)
                postal["addressRegion"] = address.State;
            if (address.PostalCode is not null)
                postal["postalCode"] = address.PostalCode;
            data["address"] = postal;
        }

        if (store.OpeningHours.Count > 0)
            data["openingHoursSpecification"] = store.OpeningHours.Select(opening => new Dictionary<string, object?>
            {
                ["@type"] = "OpeningHoursSpecification",
                ["dayOfWeek"] = $"https://schema.org/{opening.Day}",
                // Open the whole day: from midnight to the last minute, as search engines read it.
                ["opens"] = opening.Opens.ToString("HH:mm", CultureInfo.InvariantCulture),
                ["closes"] = (opening.Opens == opening.Closes ? new TimeOnly(23, 59) : opening.Closes).ToString("HH:mm", CultureInfo.InvariantCulture)
            }).ToList();
        return data;
    }

    private static string SchemaTypeOf(BusinessCategory category) => category switch
    {
        BusinessCategory.Food => "Restaurant",
        BusinessCategory.Groceries => "GroceryStore",
        BusinessCategory.Apparel => "ClothingStore",
        BusinessCategory.Electronics => "ElectronicsStore",
        BusinessCategory.Hardware => "HardwareStore",
        BusinessCategory.Health or BusinessCategory.Stationery => "Store",
        BusinessCategory.Beauty => "HealthAndBeautyBusiness",
        BusinessCategory.Automotive => "AutomotiveBusiness",
        _ => "LocalBusiness"
    };

    /// <summary>An address of the app or of the storage, whole: uploads on this server come as /uploads/....</summary>
    private static string Absolute(string baseUrl, string url)
        => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? url
            : baseUrl + (url.StartsWith('/') ? url : "/" + url);

    /// <summary>On one line and within what search engines show.</summary>
    private static string Shorten(string text)
    {
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= MaxDescriptionLength ? line : line[..(MaxDescriptionLength - 1)].TrimEnd() + "…";
    }

    // Escapes <, >, &, and both quotes, and leaves accents as they are: «Café» stays readable in the page.
    private static readonly HtmlEncoder HtmlText = HtmlEncoder.Create(UnicodeRanges.All);

    private static string Html(string value) => HtmlText.Encode(value);
}

/// <summary>
/// The categories of Explorar and the address of their pages (/explorar/{slug}), as the client names them
/// (client/src/utils/businessCategories.ts, compared by <c>ExploreCategoriesTests</c>).
/// </summary>
public static class ExploreCategories
{
    public sealed record Category(BusinessCategory Value, string Label, string Slug);

    public static readonly IReadOnlyList<Category> All =
    [
        new(BusinessCategory.Food, "Comida", "comida"),
        new(BusinessCategory.Groceries, "Abarrotes", "abarrotes"),
        new(BusinessCategory.Health, "Farmacia y salud", "salud"),
        new(BusinessCategory.Apparel, "Ropa y accesorios", "ropa"),
        new(BusinessCategory.Electronics, "Electrónica", "electronica"),
        new(BusinessCategory.Hardware, "Ferretería y hogar", "ferreteria"),
        new(BusinessCategory.Stationery, "Papelería", "papeleria"),
        new(BusinessCategory.Beauty, "Belleza y bienestar", "belleza"),
        new(BusinessCategory.Automotive, "Autos", "autos"),
        new(BusinessCategory.Services, "Servicios", "servicios"),
        new(BusinessCategory.Other, "Otros", "otros")
    ];

    public static Category? Of(BusinessCategory value) => All.FirstOrDefault(category => category.Value == value);

    public static Category? BySlug(string slug)
        => All.FirstOrDefault(category => category.Slug.Equals(slug.Trim(), StringComparison.OrdinalIgnoreCase));
}
