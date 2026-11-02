using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FoodParkDigital.Infrastructure.Tests.Support;
using static FoodParkDigital.WebApi.Tests.CrossTenantHomeVisitBookingSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The photos of what happens a customer sends with a visit requested online (Fase 29H, ADR 0062) through the API: anonymous,
/// with the link of the request in the form; the answer says how many, never the photos or who the customer is; only its
/// business sees them in its agenda and opens them with its session, and the other business, or no session, never does.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantVisitProblemPhotosSqlServerTests
{
    private const string Photos = "/api/v1/public/appointments/tracking/photos";

    private readonly TwoBusinessesApi _api;

    public CrossTenantVisitProblemPhotosSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task APhotoSentFromTheLink_ShouldBeSeenAndOpenedOnlyByItsBusiness()
    {
        var mine = await _api.SignUpAsync("fotos-a");
        var theirs = await _api.SignUpAsync("fotos-b");
        var slug = await ReceiveVisitsAsync(mine.Client, "72000", 50m, chargePolicy: 1);
        using var stranger = _api.AnonymousClient();
        var receipt = await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync(
            $"/api/v1/public/menu/{slug}/home-visits", Request(await FirstFreeTimeAsync(stranger, slug), "72000")));
        var token = receipt.GetProperty("trackingToken").GetString()!;

        var sent = await TwoBusinessesApi.ReadAsync(await stranger.PostAsync(Photos, Form(token, TwoBusinessesApi.TinyPng, "image/png", "fuga.png")));

        Assert.Equal((1, true), (sent.GetProperty("problemPhotos").GetInt32(), sent.GetProperty("canAddPhotos").GetBoolean()));
        // The link shows neither the photo nor who the customer is.
        Assert.DoesNotContain("/files/", sent.GetRawText());
        Assert.DoesNotContain("5522107765", sent.GetRawText());
        Assert.DoesNotContain("Juárez", sent.GetRawText());

        // Its agenda has it, and its session opens it as an image never rendered as a page.
        var request = Assert.Single((await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync("/api/v1/appointments?status=5")))
            .GetProperty("items").EnumerateArray());
        var path = Assert.Single(request.GetProperty("problemPhotos").EnumerateArray()).GetString()!;
        Assert.StartsWith("/files/orders/", path);
        var opened = await mine.Client.GetAsync($"/api/v1{path}");
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Equal("image/webp", opened.Content.Headers.ContentType?.MediaType);
        Assert.Equal("sandbox", opened.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("no-store", opened.Headers.CacheControl?.ToString());

        // The other business does not see it nor open it; without a session nothing opens.
        Assert.Empty((await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync("/api/v1/appointments?status=5")))
            .GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync($"/api/v1{path}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync($"/api/v1{path}")).StatusCode);

        // A link that does not exist, or what is not a photo, takes nothing.
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.PostAsync(Photos, Form(new string('z', 43), TwoBusinessesApi.TinyPng, "image/png", "fuga.png"))).StatusCode);
        var html = await stranger.PostAsync(Photos, Form(token, Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"), "image/png", "fuga.png"));
        Assert.Equal(HttpStatusCode.BadRequest, html.StatusCode);
        Assert.Contains("Manda una foto JPG, PNG o WebP.", await html.Content.ReadAsStringAsync());
        var nothing = await stranger.PostAsync(Photos, new MultipartFormDataContent { { new StringContent(token), "token" } });
        Assert.Equal(HttpStatusCode.BadRequest, nothing.StatusCode);
        var tracked = await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync("/api/v1/public/appointments/tracking", new { token }));
        Assert.Equal(1, tracked.GetProperty("problemPhotos").GetInt32());
    }

    private static MultipartFormDataContent Form(string token, byte[] file, string contentType, string fileName)
    {
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { new StringContent(token), "token" }, { content, "file", fileName } };
    }
}
