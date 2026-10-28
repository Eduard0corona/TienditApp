using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.WebApi.Http;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The public pages as search engines and link previews read them (Fase 24D-2, ADR 0027): a store, its booking page and
/// Explorar come with their own title, description, image and address in index.html, a store with its structured data;
/// a store kept out of Explorar keeps its preview but stays out of search engines; every other page of the app stays out
/// of them; robots.txt and sitemap.xml say what to read. What the stores answer comes from a fake mediator.
/// </summary>
public sealed class PublicPagesTests : IClassFixture<PublicPagesTests.PagesApi>
{
    private readonly PagesApi _api;

    public PublicPagesTests(PagesApi api)
    {
        _api = api;
        _api.Sender.Reset();
    }

    [Fact]
    public async Task AStore_ShouldComeWithItsTitleDescriptionImageAndAddress_AndItsStructuredData()
    {
        _api.Sender.Store = Store();

        using var response = await _api.Http.GetAsync("/m/cafe-luna?mesa=Mesa%204");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
        Assert.Equal("cafe-luna", Assert.IsType<GetStorePreviewQuery>(Assert.Single(_api.Sender.Requests)).Slug);
        // Its own title in place of the app's, and the app itself still loads.
        Assert.Single(Regex.Matches(html, "<title>"));
        Assert.Contains("<title>Café Luna: pide en línea · TienditApp</title>", html);
        Assert.Contains("/assets/index-abc123.js", html);
        Assert.Equal("Café de olla y pan dulce. En Puebla.", Meta(html, "name", "description"));
        Assert.Equal("index, follow", Meta(html, "name", "robots"));
        // The address of the page without the table of its QR.
        Assert.Contains("<link rel=\"canonical\" href=\"https://app.tienditapp.mx/m/cafe-luna\">", html);
        Assert.Equal("https://app.tienditapp.mx/m/cafe-luna", Meta(html, "property", "og:url"));
        Assert.Equal("Café Luna: pide en línea · TienditApp", Meta(html, "property", "og:title"));
        Assert.Equal("https://app.tienditapp.mx/uploads/covers/luna/portada_lg.webp", Meta(html, "property", "og:image"));
        Assert.Equal("summary_large_image", Meta(html, "name", "twitter:card"));

        using var data = StructuredData(html);
        var store = data.RootElement;
        Assert.Equal("https://schema.org", store.GetProperty("@context").GetString());
        Assert.Equal("Restaurant", store.GetProperty("@type").GetString());
        Assert.Equal("Café Luna", store.GetProperty("name").GetString());
        Assert.Equal("https://app.tienditapp.mx/m/cafe-luna", store.GetProperty("hasMenu").GetString());
        // A Mexican number with its country code.
        Assert.Equal("+522221234567", store.GetProperty("telephone").GetString());
        Assert.Equal(
            ["https://app.tienditapp.mx/uploads/covers/luna/portada_lg.webp", "https://cdn.tienditapp.mx/logos/luna/logo_md.webp"],
            store.GetProperty("image").EnumerateArray().Select(image => image.GetString()));
        var address = store.GetProperty("address");
        Assert.Equal("Av. Juárez 120, Local 3", address.GetProperty("streetAddress").GetString());
        Assert.Equal("Puebla", address.GetProperty("addressLocality").GetString());
        Assert.Equal("72000", address.GetProperty("postalCode").GetString());
        Assert.Equal("MX", address.GetProperty("addressCountry").GetString());
        // Where its owner put it on the map (Fase 24G), as numbers.
        var geo = store.GetProperty("geo");
        Assert.Equal("GeoCoordinates", geo.GetProperty("@type").GetString());
        Assert.Equal((19.043577m, -98.198626m), (geo.GetProperty("latitude").GetDecimal(), geo.GetProperty("longitude").GetDecimal()));
        var hours = store.GetProperty("openingHoursSpecification").EnumerateArray().ToList();
        Assert.Equal(4, hours.Count);
        Assert.Equal("https://schema.org/Monday", hours[0].GetProperty("dayOfWeek").GetString());
        Assert.Equal("09:00", hours[0].GetProperty("opens").GetString());
        // Past midnight it closes before it opens.
        Assert.Equal("02:00", hours[1].GetProperty("closes").GetString());
        // 24 hours from midnight: the whole day, to the last minute.
        Assert.Equal("00:00", hours[2].GetProperty("opens").GetString());
        Assert.Equal("23:59", hours[2].GetProperty("closes").GetString());
        // 24 hours from 08:00: to the minute before it opens the next day, none of its hours lost.
        Assert.Equal("https://schema.org/Sunday", hours[3].GetProperty("dayOfWeek").GetString());
        Assert.Equal("08:00", hours[3].GetProperty("opens").GetString());
        Assert.Equal("07:59", hours[3].GetProperty("closes").GetString());
    }

    [Fact]
    public async Task WhatTheOwnerWrote_ShouldReadAsSearchEnginesExpectIt()
    {
        _api.Sender.Store = Store(store =>
        {
            store.Description = "¡Tacos al pastor!";
            store.Address!.Country = "Mexico";
            store.ContactPhone = "+52 222 123 4567";
        });

        using var response = await _api.Http.GetAsync("/m/cafe-luna");
        var html = await response.Content.ReadAsStringAsync();

        // A line that already ends a sentence keeps its own mark.
        Assert.Equal("¡Tacos al pastor! En Puebla.", Meta(html, "name", "description"));
        using var data = StructuredData(html);
        Assert.Equal("MX", data.RootElement.GetProperty("address").GetProperty("addressCountry").GetString());
        // A phone already written with its country code stays as the owner wrote it.
        Assert.Equal("+52 222 123 4567", data.RootElement.GetProperty("telephone").GetString());

        _api.Sender.Store = Store(store =>
        {
            // Over what search engines show, cut right where an emoji (two characters) begins.
            store.Description = new string('a', 158) + "🌮🌮";
            store.Address!.Country = "Guatemala";
        });
        using var longer = await _api.Http.GetAsync("/m/cafe-luna");
        html = await longer.Content.ReadAsStringAsync();

        Assert.Equal(new string('a', 158) + "…", Meta(html, "name", "description"));
        using var other = StructuredData(html);
        Assert.Equal("Guatemala", other.RootElement.GetProperty("address").GetProperty("addressCountry").GetString());
    }

    [Fact]
    public async Task WhatABusinessWrites_ShouldNeverLeaveItsTag()
    {
        _api.Sender.Store = Store(store =>
        {
            store.BusinessName = "Luna\"><script>alert(1)</script>";
            store.Description = "Pan </script><img src=x onerror=alert(2)> & café";
            store.Address = null;
            store.Geo = null;
            store.ContactPhone = "</script>";
        });

        using var response = await _api.Http.GetAsync("/m/cafe-luna");
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<script>alert(1)", html);
        Assert.DoesNotContain("<img src=x", html);
        // The only closing script tags are the app's own and the structured data's.
        Assert.Equal(2, Regex.Matches(html, "</script>", RegexOptions.IgnoreCase).Count);
        Assert.Contains("<title>Luna&quot;&gt;&lt;script&gt;alert(1)&lt;/script&gt;: pide en línea · TienditApp</title>", html);
        Assert.Equal("Pan </script><img src=x onerror=alert(2)> & café.", Meta(html, "name", "description"));
        using var data = StructuredData(html);
        Assert.Equal("Luna\"><script>alert(1)</script>", data.RootElement.GetProperty("name").GetString());
        Assert.Equal("</script>", data.RootElement.GetProperty("telephone").GetString());
        Assert.False(data.RootElement.TryGetProperty("address", out _));
        Assert.False(data.RootElement.TryGetProperty("geo", out _));
    }

    [Fact]
    public async Task AStoreOutOfExplorar_ShouldKeepItsPreview_AndStayOutOfSearchEngines()
    {
        _api.Sender.Store = Store(store =>
        {
            store.ListedInDirectory = false;
            store.CoverImageUrl = null;
            store.TakesOrders = false;
            store.Description = null;
        });

        using var response = await _api.Http.GetAsync("/m/cafe-luna");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.Equal("noindex", Meta(html, "name", "robots"));
        Assert.DoesNotContain("rel=\"canonical\"", html);
        Assert.Contains("<title>Café Luna: menú en línea · TienditApp</title>", html);
        Assert.Equal("Mira el menú de Café Luna en línea. En Puebla.", Meta(html, "name", "description"));
        // Its logo for the preview of the link: a square, so the small card.
        Assert.Equal("https://cdn.tienditapp.mx/logos/luna/logo_md.webp", Meta(html, "property", "og:image"));
        Assert.Equal("summary", Meta(html, "name", "twitter:card"));
    }

    [Fact]
    public async Task ABookingPage_ShouldBeListedOnlyWhileTheStoreBooksOnline()
    {
        _api.Sender.Store = Store(store => store.BooksOnline = true);

        using var booking = await _api.Http.GetAsync("/m/cafe-luna/agendar");
        var html = await booking.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, booking.StatusCode);
        Assert.Contains("<title>Agenda tu cita en Café Luna · TienditApp</title>", html);
        Assert.Contains("<link rel=\"canonical\" href=\"https://app.tienditapp.mx/m/cafe-luna/agendar\">", html);
        Assert.Equal("Elige el servicio, el día y la hora, y agenda tu cita en Café Luna. En Puebla.", Meta(html, "name", "description"));
        Assert.DoesNotContain("ld+json", html);

        _api.Sender.Store = Store(store => store.BooksOnline = false);
        using var notBooking = await _api.Http.GetAsync("/m/cafe-luna/agendar");
        Assert.Equal("noindex", Assert.Single(notBooking.Headers.GetValues("X-Robots-Tag")));
    }

    [Fact]
    public async Task AStoreThatOnlyBooks_ShouldSayItsPageIsToBook()
    {
        _api.Sender.Store = Store(store =>
        {
            store.TakesOrders = false;
            store.BooksOnline = true;
            store.Description = null;
            store.Category = BusinessCategory.Beauty;
        });

        using var response = await _api.Http.GetAsync("/m/cafe-luna");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("<title>Café Luna: agenda tu cita · TienditApp</title>", html);
        Assert.Equal("Mira los servicios de Café Luna y agenda tu cita en línea. En Puebla.", Meta(html, "name", "description"));
        using var data = StructuredData(html);
        Assert.Equal("HealthAndBeautyBusiness", data.RootElement.GetProperty("@type").GetString());
        Assert.False(data.RootElement.TryGetProperty("hasMenu", out _));
    }

    [Fact]
    public async Task AStoreAskedWithHead_ShouldAnswerAsItsPage_WithoutTheBody()
    {
        _api.Sender.Store = Store(store => store.ListedInDirectory = false);

        using var response = await _api.Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/m/cafe-luna"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AnUnknownStore_ShouldBeNotFound_WithThePageThatSaysSo()
    {
        _api.Sender.Store = null;

        using var response = await _api.Http.GetAsync("/m/no-existe");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.Contains("<title>Menú no encontrado · TienditApp</title>", html);
        Assert.Contains("/assets/index-abc123.js", html);
    }

    [Fact]
    public async Task ASlugNoBusinessCanHave_ShouldBeNotFound_WithoutAskingForIt()
    {
        using var response = await _api.Http.GetAsync("/m/" + new string('a', 101));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_api.Sender.Requests);
    }

    [Fact]
    public async Task AStoreThatCannotBeRead_ShouldStillOpen_AndAskSearchEnginesToComeBack()
    {
        _api.Sender.Failure = new TimeoutException("Simulated database outage.");

        using var response = await _api.Http.GetAsync("/m/cafe-luna");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("120", response.Headers.RetryAfter?.ToString());
        // Not a page to forget: the next visit reads it again.
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
        Assert.Contains("<title>TienditApp</title>", html);
        Assert.Contains("/assets/index-abc123.js", html);
    }

    [Theory]
    [InlineData("/explorar", "<title>Explorar · TienditApp</title>", "https://app.tienditapp.mx/explorar")]
    [InlineData("/explorar/comida", "<title>Comida · Explorar · TienditApp</title>", "https://app.tienditapp.mx/explorar/comida")]
    [InlineData("/explorar/Ferreteria/", "<title>Ferretería y hogar · Explorar · TienditApp</title>", "https://app.tienditapp.mx/explorar/ferreteria")]
    public async Task Explorar_ShouldComeWithTheHeadOfItsPage_WithoutReadingTheStores(string path, string title, string canonical)
    {
        using var response = await _api.Http.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
        Assert.Contains(title, html);
        Assert.Contains($"<link rel=\"canonical\" href=\"{canonical}\">", html);
        Assert.Empty(_api.Sender.Requests);
    }

    [Fact]
    public async Task TheRoot_ShouldComeWithTheHeadOfThePageForBusinesses_WithoutReadingTheStores()
    {
        using var response = await _api.Http.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
        Assert.Contains("<title>TienditApp para negocios: punto de venta, menú en línea y facturación</title>", html);
        Assert.Contains("<link rel=\"canonical\" href=\"https://app.tienditapp.mx/\">", html);
        // Whole, without the ellipsis of a description cut to what search engines show.
        Assert.Equal(
            "Vende en mostrador y en línea, controla inventario y caja, recibe pedidos a domicilio y factura CFDI desde un solo lugar. Crea la cuenta de tu negocio gratis.",
            Meta(html, "name", "description"));
        Assert.Equal("index, follow", Meta(html, "name", "robots"));
        Assert.Empty(_api.Sender.Requests);
    }

    [Theory]
    [InlineData("/negocios")]
    [InlineData("/Negocios/")]
    public async Task ThePageForBusinessesAtItsOwnAddress_ShouldPreviewAsTheRoot_AndStayOutOfSearchEngines(string path)
    {
        using var response = await _api.Http.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.Contains("<title>TienditApp para negocios: punto de venta, menú en línea y facturación</title>", html);
        Assert.Equal("noindex", Meta(html, "name", "robots"));
        Assert.DoesNotContain("rel=\"canonical\"", html);
        Assert.Equal("https://app.tienditapp.mx/", Meta(html, "property", "og:url"));
        Assert.Empty(_api.Sender.Requests);
    }

    [Fact]
    public async Task TheCategoryOfOtherBusinesses_ShouldSayItInWords()
    {
        using var response = await _api.Http.GetAsync("/explorar/otros");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("<title>Otros · Explorar · TienditApp</title>", html);
        Assert.Equal("Más negocios en TienditApp: mira su menú, pide en línea o agenda tu cita.", Meta(html, "name", "description"));
    }

    [Fact]
    public async Task ACategoryThatDoesNotExist_ShouldStayOutOfSearchEngines()
    {
        using var response = await _api.Http.GetAsync("/explorar/no-existe");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
    }

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/login")]
    [InlineData("/mis-pedidos/123")]
    [InlineData("/index.html")]
    public async Task EveryOtherPageOfTheApp_ShouldStayOutOfSearchEngines(string path)
    {
        using var response = await _api.Http.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.Contains("<title>TienditApp</title>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Robots_ShouldLetSearchEnginesReadThePublicApi_AndPointToTheSitemap()
    {
        using var response = await _api.Http.GetAsync("/robots.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "User-agent: *\nAllow: /api/v1/public/\nDisallow: /api/\nDisallow: /hubs/\n\nSitemap: https://app.tienditapp.mx/sitemap.xml\n",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheSitemap_ShouldListTheRoot_Explorar_ItsCategoriesWithBusinesses_AndEachStore()
    {
        _api.Sender.Sitemap = new PublicSitemapDto
        {
            Categories = [BusinessCategory.Food, BusinessCategory.Beauty],
            Stores = [new() { Slug = "cafe-luna" }, new() { Slug = "salon-sol", BooksOnline = true }]
        };

        using var response = await _api.Http.GetAsync("/sitemap.xml");
        var sitemap = XDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        Assert.Equal(
            [
                "https://app.tienditapp.mx/",
                "https://app.tienditapp.mx/explorar",
                "https://app.tienditapp.mx/explorar/comida",
                "https://app.tienditapp.mx/explorar/belleza",
                "https://app.tienditapp.mx/m/cafe-luna",
                "https://app.tienditapp.mx/m/salon-sol",
                "https://app.tienditapp.mx/m/salon-sol/agendar"
            ],
            sitemap.Descendants(ns + "loc").Select(loc => loc.Value));
    }

    [Fact]
    public async Task ASitemapThatCannotBeRead_ShouldAskSearchEnginesToComeBack()
    {
        _api.Sender.Failure = new TimeoutException("Simulated database outage.");

        using var response = await _api.Http.GetAsync("/sitemap.xml");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("120", response.Headers.RetryAfter?.ToString());
    }

    [Fact]
    public async Task AStoreOverItsLimit_ShouldStillOpenTheApp_AndAskSearchEnginesToComeBack()
    {
        // A limiter of its own, so the other tests keep theirs; Development allows 600 a minute per IP.
        await using var factory = PagesApi.CreateFactory(_api.WebRoot, _api.Sender, "https://app.tienditapp.mx/");
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        _api.Sender.Store = Store();

        HttpResponseMessage? rejected = null;
        for (var request = 0; request < 601 && rejected is null; request++)
        {
            var response = await http.GetAsync("/m/cafe-luna");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                rejected = response;
            else
                response.Dispose();
        }

        Assert.NotNull(rejected);
        using (rejected)
        {
            var html = await rejected.Content.ReadAsStringAsync();
            Assert.Equal("text/html", rejected.Content.Headers.ContentType?.MediaType);
            Assert.NotNull(rejected.Headers.RetryAfter);
            // Not a page to forget, and the app itself still loads (its menu has a limit of its own).
            Assert.False(rejected.Headers.Contains("X-Robots-Tag"));
            Assert.Contains("<title>TienditApp</title>", html);
            Assert.Contains("/assets/index-abc123.js", html);
        }
    }

    [Fact]
    public void TheHeadOfAPage_ShouldReplaceTheTitleOfTheAppsOwnIndex_AndGoInItsHead()
    {
        var index = File.ReadAllText(Path.Combine(RepositoryRoot(), "client", "index.html"));
        var head = PublicPages.StoreHead(Store(), booking: false);

        var html = PublicPages.WithHead(index, head.Render("https://app.tienditapp.mx"));

        Assert.Single(Regex.Matches(html, "<title>"));
        Assert.Contains("<title>Café Luna: pide en línea · TienditApp</title>", html);
        var close = html.IndexOf("</head>", StringComparison.Ordinal);
        Assert.InRange(html.IndexOf("<link rel=\"canonical\"", StringComparison.Ordinal), 0, close);
        Assert.InRange(html.IndexOf("application/ld+json", StringComparison.Ordinal), 0, close);
        // Everything else of the app stays as it is.
        Assert.Contains("<script type=\"module\" src=\"/src/main.tsx\"></script>", html);
        Assert.Contains("<meta name=\"theme-color\"", html);
    }

    [Fact]
    public async Task WithoutAppUrl_TheAddressesShouldBeTheOnesTheRequestCameTo()
    {
        await using var factory = PagesApi.CreateFactory(_api.WebRoot, _api.Sender, appUrl: null);
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var robots = await http.GetStringAsync("/robots.txt");

        // Development keeps the scheme the request came with; outside it, always https.
        Assert.EndsWith("Sitemap: http://localhost/sitemap.xml\n", robots);
    }

    [Fact]
    public void TheCategoriesOfExplorar_ShouldHaveTheAddressesTheClientGivesThem()
    {
        var client = File.ReadAllText(Path.Combine(RepositoryRoot(), "client", "src", "utils", "businessCategories.ts"));
        var entries = Regex.Matches(client, @"value: BusinessCategory\.(\w+), label: '([^']+)', slug: '([^']+)'")
            .Select(match => (Enum.Parse<BusinessCategory>(match.Groups[1].Value), match.Groups[2].Value, match.Groups[3].Value))
            .ToList();

        Assert.Equal(Enum.GetValues<BusinessCategory>().Length, entries.Count);
        Assert.Equal(entries, ExploreCategories.All.Select(category => (category.Value, category.Label, category.Slug)));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FoodParkDigital.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("FoodParkDigital.sln was not found above the test output.");
    }

    private static StorePreviewDto Store(Action<StorePreviewDto>? change = null)
    {
        var store = new StorePreviewDto
        {
            BusinessName = "Café Luna",
            Slug = "cafe-luna",
            Description = "Café de olla y pan dulce",
            Category = BusinessCategory.Food,
            LogoUrl = "https://cdn.tienditapp.mx/logos/luna/logo_md.webp",
            CoverImageUrl = "/uploads/covers/luna/portada_lg.webp",
            ListedInDirectory = true,
            TakesOrders = true,
            Address = new StoreAddressDto { Street = "Av. Juárez 120, Local 3", City = "Puebla", State = "Puebla", PostalCode = "72000" },
            Geo = new MapPointDto { Latitude = 19.043577m, Longitude = -98.198626m },
            ContactPhone = "2221234567",
            OpeningHours =
            [
                new() { Day = DayOfWeek.Monday, Opens = new TimeOnly(9, 0), Closes = new TimeOnly(15, 0) },
                new() { Day = DayOfWeek.Friday, Opens = new TimeOnly(18, 0), Closes = new TimeOnly(2, 0) },
                new() { Day = DayOfWeek.Saturday, Opens = new TimeOnly(0, 0), Closes = new TimeOnly(0, 0) },
                new() { Day = DayOfWeek.Sunday, Opens = new TimeOnly(8, 0), Closes = new TimeOnly(8, 0) }
            ]
        };
        change?.Invoke(store);
        return store;
    }

    /// <summary>The content of the meta tag with that attribute and name, unescaped.</summary>
    private static string? Meta(string html, string attribute, string name)
    {
        var match = Regex.Match(html, $"<meta {attribute}=\"{Regex.Escape(name)}\" content=\"([^\"]*)\">");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    private static JsonDocument StructuredData(string html)
    {
        var match = Regex.Match(html, "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline);
        Assert.True(match.Success, "The page carries no structured data.");
        return JsonDocument.Parse(match.Groups[1].Value);
    }

    /// <summary>The API over a web root with the app's index.html, without a database, its stores from a fake mediator.</summary>
    public sealed class PagesApi : IDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;

        public PagesApi()
        {
            WebRoot = Path.Combine(Path.GetTempPath(), "fpd-public-pages", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(WebRoot);
            File.WriteAllText(Path.Combine(WebRoot, "index.html"),
                "<!doctype html>\n<html lang=\"es\">\n  <head>\n    <meta charset=\"UTF-8\" />\n    <title>TienditApp</title>\n"
                + "    <script type=\"module\" src=\"/assets/index-abc123.js\"></script>\n  </head>\n  <body><div id=\"root\"></div></body>\n</html>\n");
            _factory = CreateFactory(WebRoot, Sender, "https://app.tienditapp.mx/");
            Http = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        }

        public string WebRoot { get; }

        public FakeSender Sender { get; } = new();

        public HttpClient Http { get; }

        public static WebApplicationFactory<Program> CreateFactory(string webRoot, FakeSender sender, string? appUrl)
            => new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            {
                host.UseEnvironment("Development");
                host.UseWebRoot(webRoot);
                host.UseSetting(WebHostDefaults.StaticWebAssetsKey, Path.Combine(webRoot, "sin-manifiesto.json"));
                host.UseSetting("ConnectionStrings:DefaultConnection", "Server=127.0.0.1,1;Database=SinBase;Encrypt=False");
                host.UseSetting("Database:AutoMigrateOnStartup", "false");
                host.UseSetting("Database:SeedOnStartup", "false");
                host.UseSetting("BackgroundJobs:Enabled", "false");
                host.UseSetting("AppUrl", appUrl ?? string.Empty);
                host.ConfigureServices(services => services.AddSingleton<ISender>(sender));
            });

        public void Dispose()
        {
            Http.Dispose();
            _factory.Dispose();
            Directory.Delete(WebRoot, recursive: true);
        }
    }

    /// <summary>Answers the store and the sitemap the test set, and records what the pages asked.</summary>
    public sealed class FakeSender : ISender
    {
        public StorePreviewDto? Store { get; set; }
        public PublicSitemapDto Sitemap { get; set; } = new();
        public Exception? Failure { get; set; }
        public List<object> Requests { get; } = [];

        public void Reset()
        {
            Store = null;
            Sitemap = new PublicSitemapDto();
            Failure = null;
            Requests.Clear();
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Failure is not null)
                throw Failure;
            object? response = request switch
            {
                GetStorePreviewQuery => Store,
                GetPublicSitemapQuery => Sitemap,
                _ => throw new NotSupportedException($"Unexpected request {request.GetType().Name}.")
            };
            return Task.FromResult((TResponse)response!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
