using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Infrastructure.Tests.Support;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// How soon a business answers, in Explorar (Fase 29J, ADR 0064), through the API: the requests a customer makes on its
/// booking page and its team accepts or declines in its agenda are what its card says, never those of another business.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantDirectoryResponseTimeSqlServerTests
{
    private readonly TwoBusinessesApi _api;

    public CrossTenantDirectoryResponseTimeSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Card_ShouldSayHowSoonItsTeamAnswered_ItsOwnRequestsOnly()
    {
        var quick = await _api.SignUpAsync("respuesta-a");
        var (quickSlug, quickService) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(quick.Client, "Corte respuesta29j");
        var waiting = await _api.SignUpAsync("respuesta-b");
        var (waitingSlug, waitingService) = await CrossTenantOnlineBookingSqlServerTests.BookingAsync(waiting.Client, "Masaje respuesta29j");
        using var stranger = _api.AnonymousClient();

        // Three requests to each; one team answers its three at once (two accepted, one declined), the other none yet.
        var answers = new[] { "accept", "accept", "decline" };
        for (var n = 0; n < answers.Length; n++)
        {
            var mine = await RequestAsync(stranger, quickSlug, quickService, $"55100000{n:00}");
            Assert.Equal(System.Net.HttpStatusCode.OK, (await quick.Client.PostAsync($"/api/v1/appointments/{mine}/{answers[n]}", null)).StatusCode);
            await RequestAsync(stranger, waitingSlug, waitingService, $"55200000{n:00}");
        }
        _api.ForgetDirectorySnapshot();

        var listed = (await TwoBusinessesApi.ReadAsync(await stranger.GetAsync("/api/v1/public/directory?q=respuesta&pageSize=48")))
            .GetProperty("businesses").GetProperty("items").EnumerateArray()
            .ToDictionary(b => b.GetProperty("slug").GetString()!, b => b.GetProperty("responseTime"));

        Assert.Equal(0, listed[quickSlug].GetInt32());
        Assert.Equal(JsonValueKind.Null, listed[waitingSlug].ValueKind);
    }

    private static async Task<Guid> RequestAsync(HttpClient stranger, string slug, Guid service, string phone)
    {
        var startAt = await CrossTenantOnlineBookingSqlServerTests.FirstFreeAsync(stranger, slug, service);
        var receipt = await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync(
            $"/api/v1/public/menu/{slug}/appointments", CrossTenantOnlineBookingSqlServerTests.Request(service, startAt, phone)));
        var link = receipt.GetProperty("trackingToken").GetString()!;
        return (await TwoBusinessesApi.ReadAsync(await stranger.PostAsJsonAsync(
            CrossTenantOnlineBookingSqlServerTests.Tracking, new { token = link }))).GetProperty("id").GetGuid();
    }
}
