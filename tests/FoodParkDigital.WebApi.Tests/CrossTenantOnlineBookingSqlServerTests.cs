using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Infrastructure.Tests.Support;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Appointments booked online (Fase 24D, ADR 0025) through the API: a booking page offers only its business's services, a
/// request lands in that business, its link opens only it, the team of another business neither reads, accepts nor
/// declines it, and an account sees and cancels only its own. Who attends (Fase 24J) is set only for a business's own team
/// and booked only on its own page.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantOnlineBookingSqlServerTests
{
    private const string Tracking = "/api/v1/public/appointments/tracking";
    private const string Settings = "/api/v1/appointments/online-booking";

    private readonly TwoBusinessesApi _api;

    public CrossTenantOnlineBookingSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Request_ShouldStayInItsBusiness_AndBeDecidedOnlyByItsTeam()
    {
        var mine = await _api.SignUpAsync("citas-a");
        var theirs = await _api.SignUpAsync("citas-b");
        var (mySlug, myService) = await BookingAsync(mine.Client, "Corte de cabello");
        var (theirSlug, theirService) = await BookingAsync(theirs.Client, "Masaje");
        using var stranger = _api.AnonymousClient();

        // Each page offers its own service; the service of the other business has no times here.
        var page = await TwoBusinessesApi.ReadAsync(await stranger.GetAsync($"/api/v1/public/menu/{mySlug}/booking"));
        Assert.Equal([myService], page.GetProperty("services").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/v1/public/menu/{mySlug}/booking/slots?serviceId={theirService}")).StatusCode);
        var foreign = await stranger.PostAsJsonAsync($"/api/v1/public/menu/{mySlug}/appointments", Request(theirService, DateTimeOffset.UtcNow.AddDays(2), "5511112222"));
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);

        var receipt = await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync(
            $"/api/v1/public/menu/{mySlug}/appointments", Request(myService, await FirstFreeAsync(stranger, mySlug, myService), "5512345678")));
        var link = receipt.GetProperty("trackingToken").GetString()!;
        var id = (await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync(Tracking, new { token = link }))).GetProperty("id").GetGuid();

        // The team of the other business neither reads, accepts nor declines it, and its settings stay its own.
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync($"/api/v1/appointments/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsync($"/api/v1/appointments/{id}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsync($"/api/v1/appointments/{id}/decline", null)).StatusCode);
        Assert.DoesNotContain(id.ToString(), await theirs.Client.GetStringAsync("/api/v1/appointments?pageSize=100"));
        Assert.Equal(theirSlug, (await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync(Settings))).GetProperty("slug").GetString());

        // Its own team accepts it, and the link shows it so.
        var accepted = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsync($"/api/v1/appointments/{id}/accept", null));
        Assert.Equal(0, accepted.GetProperty("status").GetInt32());
        var tracked = await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync(Tracking, new { token = link }));
        Assert.Equal(("Confirmada", mySlug), (tracked.GetProperty("statusLabel").GetString(), tracked.GetProperty("businessSlug").GetString()));
        var unknown = await stranger.PostAsJsonAsync(Tracking, new { token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE" });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [SqlServerFact]
    public async Task Account_ShouldSeeAndCancelOnlyItsOwnAppointments()
    {
        var business = await _api.SignUpAsync("citas-cuenta");
        var (slug, service) = await BookingAsync(business.Client, "Manicure");
        var ana = await _api.SignUpCustomerAsync("citas-ana");
        var beto = await _api.SignUpCustomerAsync("citas-beto");
        using var stranger = _api.AnonymousClient();

        var receipt = await TwoBusinessesApi.ReadAsync(await ana.Client.PostAsJsonAsync(
            $"/api/v1/public/menu/{slug}/appointments", Request(service, await FirstFreeAsync(stranger, slug, service), "5512345678")));
        Assert.True(receipt.GetProperty("linkedToAccount").GetBoolean());

        var anas = await TwoBusinessesApi.ReadAsync(await ana.Client.GetAsync("/api/v1/me/appointments"));
        var id = Assert.Single(anas.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid();
        Assert.Equal(0, (await TwoBusinessesApi.ReadAsync(await beto.Client.GetAsync("/api/v1/me/appointments"))).GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync("/api/v1/me/appointments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await beto.Client.PostAsync($"/api/v1/me/appointments/{id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await business.Client.PostAsync($"/api/v1/me/appointments/{id}/cancel", null)).StatusCode);

        var cancelled = await TwoBusinessesApi.ReadAsync(await ana.Client.PostAsync($"/api/v1/me/appointments/{id}/cancel", null));
        Assert.Equal("La cancelaste", cancelled.GetProperty("statusLabel").GetString());
        var agenda = await TwoBusinessesApi.ReadAsync(await business.Client.GetAsync($"/api/v1/appointments/{id}"));
        Assert.True(agenda.GetProperty("cancelledByCustomer").GetBoolean());
    }

    [SqlServerFact]
    public async Task Staff_ShouldBeSetOnlyForItsOwnTeam_AndBookedOnlyOnItsOwnPage()
    {
        var mine = await _api.SignUpAsync("quien-a");
        var theirs = await _api.SignUpAsync("quien-b");
        var (mySlug, myService) = await BookingAsync(mine.Client, "Corte con persona");
        var (theirSlug, theirService) = await BookingAsync(theirs.Client, "Masaje con persona");
        using var stranger = _api.AnonymousClient();

        // Each team lists only its own people; the other business cannot set them up.
        var myTeam = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"{Settings}/staff"));
        var me = Assert.Single(myTeam.GetProperty("members").EnumerateArray()).GetProperty("userId").GetGuid();
        Assert.DoesNotContain(me.ToString(), await theirs.Client.GetStringAsync($"{Settings}/staff"));
        var person = new { enabled = true, name = "Dueña A.", allServices = true };
        Assert.Equal(HttpStatusCode.BadRequest, (await theirs.Client.PutAsJsonAsync($"{Settings}/staff/{me}", person)).StatusCode);
        await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync($"{Settings}/staff/{me}", person));

        // The person shows on their business's page only, and books only there.
        var page = await TwoBusinessesApi.ReadAsync(await stranger.GetAsync($"/api/v1/public/menu/{mySlug}/booking"));
        var staffId = Assert.Single(page.GetProperty("staff").EnumerateArray()).GetProperty("id").GetGuid();
        var theirPage = await TwoBusinessesApi.ReadAsync(await stranger.GetAsync($"/api/v1/public/menu/{theirSlug}/booking"));
        Assert.Equal(0, theirPage.GetProperty("staff").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.GetAsync($"/api/v1/public/menu/{theirSlug}/booking/slots?serviceId={theirService}&staffId={staffId}")).StatusCode);
        var elsewhere = await stranger.PostAsJsonAsync($"/api/v1/public/menu/{theirSlug}/appointments",
            Request(theirService, await FirstFreeAsync(stranger, theirSlug, theirService), "5511113333", staffId));
        Assert.Equal(HttpStatusCode.BadRequest, elsewhere.StatusCode);

        var startAt = await FirstFreeAsync(stranger, mySlug, myService, staffId);
        var receipt = await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync(
            $"/api/v1/public/menu/{mySlug}/appointments", Request(myService, startAt, "5512344321", staffId)));
        Assert.Equal("Dueña A.", receipt.GetProperty("staffName").GetString());
        var tracked = await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync(Tracking, new { token = receipt.GetProperty("trackingToken").GetString() }));
        Assert.Equal("Dueña A.", tracked.GetProperty("staffName").GetString());
        Assert.Equal(1, (await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync(Settings))).GetProperty("staffCount").GetInt32());
        Assert.Equal(0, (await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync(Settings))).GetProperty("staffCount").GetInt32());
    }

    /// <returns>The slug of the business's menu and a service it books online.</returns>
    private static async Task<(string Slug, Guid Service)> BookingAsync(HttpClient owner, string serviceName)
    {
        var slug = (await TwoBusinessesApi.ReadAsync(await owner.PutAsJsonAsync("/api/v1/auth/my-tenant/online-menu", new { mode = 1 })))
            .GetProperty("slug").GetString()!;
        // Every day of the week, 8 to 20: there are free times whatever the hour of the run.
        await TwoBusinessesApi.ReadAsync(await owner.PutAsJsonAsync("/api/v1/auth/my-tenant/opening-hours",
            new { intervals = Enumerable.Range(0, 7).Select(day => new { day, opens = "08:00", closes = "20:00" }) }));
        var category = await TwoBusinessesApi.IdAsync(owner, "/api/v1/categories", new { name = $"Servicios de {serviceName}", sortOrder = 1 });
        var service = await TwoBusinessesApi.IdAsync(owner, "/api/v1/products", new
        {
            categoryId = category,
            name = serviceName,
            priceAmount = 200m,
            priceCurrency = "MXN",
            sortOrder = 1,
            unit = 52,
            preparationTimeMinutes = 45,
            taxObject = 2
        });
        var settings = await TwoBusinessesApi.ReadAsync(await owner.PutAsJsonAsync(Settings, new { enabled = true, capacity = 1 }));
        Assert.True(settings.GetProperty("booksOnline").GetBoolean(), settings.GetRawText());
        return (slug, service);
    }

    private static async Task<DateTimeOffset> FirstFreeAsync(HttpClient client, string slug, Guid service, Guid? staffId = null)
    {
        var week = await TwoBusinessesApi.ReadAsync(await client.GetAsync(
            $"/api/v1/public/menu/{slug}/booking/slots?serviceId={service}" + (staffId is null ? "" : $"&staffId={staffId}")));
        return week.GetProperty("days").EnumerateArray()
            .SelectMany(day => day.GetProperty("slots").EnumerateArray())
            .Select(slot => slot.GetProperty("startAt").GetDateTimeOffset())
            .First();
    }

    private static object Request(Guid service, DateTimeOffset startAt, string phone, Guid? staffId = null) => new
    {
        serviceProductId = service,
        startAt,
        customerName = "Valeria Citas",
        customerPhone = phone,
        idempotencyKey = Guid.NewGuid(),
        staffId
    };
}
