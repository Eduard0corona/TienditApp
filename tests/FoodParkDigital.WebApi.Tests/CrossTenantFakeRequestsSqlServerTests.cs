using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Infrastructure.Tests.Support;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Declining a request online as fake (Fase 29V, ADR 0076) through the API: only the team of its business marks it and takes
/// the mark back, and its phone may not request again there, while it may in another business.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantFakeRequestsSqlServerTests
{
    private readonly TwoBusinessesApi _api;

    public CrossTenantFakeRequestsSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task ARequest_ShouldBeMarkedFakeOnlyByItsTeam_AndKeepItsPhoneFromAskingOnlyThere()
    {
        var mine = await _api.SignUpAsync("falsa-a");
        var theirs = await _api.SignUpAsync("falsa-b");
        var (mySlug, myService) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(mine.Client, "Corte de prueba");
        var (theirSlug, theirService) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(theirs.Client, "Masaje de prueba");
        using var stranger = _api.AnonymousClient();
        const string phone = "5512360001";
        var id = await RequestAsync(stranger, mySlug, myService, phone);

        // The team of another business neither marks it nor takes the mark back.
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsync($"/api/v1/appointments/{id}/mark-fake", null)).StatusCode);

        var marked = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsync($"/api/v1/appointments/{id}/mark-fake", null));
        Assert.Equal((99, true), (marked.GetProperty("status").GetInt32(), marked.GetProperty("markedFake").GetBoolean()));
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsync($"/api/v1/appointments/{id}/unmark-fake", null)).StatusCode);

        // Its phone may not ask again in that business, but may in another one.
        var again = await stranger.PostAsJsonAsync(
            $"/api/v1/public/menu/{mySlug}/appointments",
            CrossTenantOnlineBookingSqlServerTests.Request(myService, await CrossTenantOnlineBookingSqlServerTests.FirstFreeAsync(stranger, mySlug, myService), phone));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(RequestPublicAppointmentCommandHandler.MarkedFakeMessage, await DetailAsync(again));
        await RequestAsync(stranger, theirSlug, theirService, phone);

        // Taken back by its team, its phone may ask again.
        var unmarked = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsync($"/api/v1/appointments/{id}/unmark-fake", null));
        Assert.Equal((99, false), (unmarked.GetProperty("status").GetInt32(), unmarked.GetProperty("markedFake").GetBoolean()));
        await RequestAsync(stranger, mySlug, myService, phone);
    }

    /// <summary>A customer's request online at <paramref name="slug"/>, at its first free time; it must be stored.</summary>
    private static async Task<Guid> RequestAsync(HttpClient customer, string slug, Guid service, string phone)
    {
        var receipt = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync(
            $"/api/v1/public/menu/{slug}/appointments",
            CrossTenantOnlineBookingSqlServerTests.Request(service, await CrossTenantOnlineBookingSqlServerTests.FirstFreeAsync(customer, slug, service), phone)));
        return (await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync(
                CrossTenantOnlineBookingSqlServerTests.Tracking, new { token = receipt.GetProperty("trackingToken").GetString() })))
            .GetProperty("id").GetGuid();
    }

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString();
}
