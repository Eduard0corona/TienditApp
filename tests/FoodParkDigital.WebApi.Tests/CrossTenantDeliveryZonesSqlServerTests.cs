using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.Delivery;
using FoodParkDigital.Infrastructure.Tests.Support;
using FoodParkDigital.WebApi.Errors;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The delivery zones (Fase 22B) through the API: each business lists, saves and deletes only its own zones, the business
/// header of an owner changes nothing, a postal code can be in a zone of each business, a stale save answers 409, and the
/// team reads the zones while only owners and admins change them.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantDeliveryZonesSqlServerTests
{
    private const string Password = "Secreto1";
    private const string Zones = "/api/v1/delivery-zones";

    private readonly TwoBusinessesApi _api;

    public CrossTenantDeliveryZonesSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Zones_OfAnotherBusiness_ShouldBeInvisible_AndUntouchable()
    {
        var mine = await _api.SignUpAsync("zonas-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("zonas-b", TwoBusinessesApi.FoodBusiness);
        var myZone = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(Zones, Zone("Centro", 35m, "72000", "72005")));
        var myZoneId = myZone.GetProperty("id").GetGuid();

        // The other owner, even with the header of the business (only the platform admin may use it), lists only their
        // own zones, and can have the same postal code in one of theirs.
        using var read = new HttpRequestMessage(HttpMethod.Get, Zones);
        read.Headers.Add("X-Tenant-Id", mine.TenantId.ToString());
        Assert.Empty((await TwoBusinessesApi.ReadAsync(await theirs.Client.SendAsync(read))).EnumerateArray());
        await TwoBusinessesApi.ReadAsync(await theirs.Client.PostAsJsonAsync(Zones, Zone("Todo Puebla", 50m, "72000")));

        // Saving or deleting my zone, with the header too, finds no such zone.
        using var save = new HttpRequestMessage(HttpMethod.Put, $"{Zones}/{myZoneId}") { Content = JsonContent.Create(Zone("Mía", 1m, "72000") with { Revision = 1 }) };
        save.Headers.Add("X-Tenant-Id", mine.TenantId.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(save)).StatusCode);
        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"{Zones}/{myZoneId}?revision=1");
        delete.Headers.Add("X-Tenant-Id", mine.TenantId.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(delete)).StatusCode);

        // Mine is as I left it, and each business's zones and postal codes stay in it.
        var myList = await mine.Client.GetFromJsonAsync<JsonElement>(Zones);
        var listed = Assert.Single(myList.EnumerateArray());
        Assert.Equal(("Centro", 35m), (listed.GetProperty("name").GetString(), listed.GetProperty("fee").GetDecimal()));
        Assert.Equal(["72000", "72005"], listed.GetProperty("postalCodes").EnumerateArray().Select(code => code.GetString()));
        Assert.Equal(
            new[] { mine.TenantId, mine.TenantId },
            await _api.QueryAsync(mine.TenantId, db => db.DeliveryZonePostalCodes.Select(code => code.TenantId).ToListAsync()));
        Assert.Equal(
            new[] { (theirs.TenantId, "Todo Puebla") },
            (await _api.QueryAsync(theirs.TenantId, db => db.DeliveryZones.ToListAsync())).Select(zone => (zone.TenantId, zone.Name)));
    }

    [SqlServerFact]
    public async Task Zone_ShouldBeSavedWithTheRevisionSeen_RefuseAPostalCodeOfAnotherZone_AndBeDeleted()
    {
        var owner = await _api.SignUpAsync("zonas-flujo", TwoBusinessesApi.FoodBusiness);
        var centro = await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync(Zones, Zone("Centro", 35m, "72000")));
        var id = centro.GetProperty("id").GetGuid();

        var saved = await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync($"{Zones}/{id}", Zone("Centro", 40m, "72000", "72005") with { Revision = 1 }));
        Assert.Equal((40m, 2), (saved.GetProperty("fee").GetDecimal(), saved.GetProperty("revision").GetInt32()));

        // A tab that still shows revision 1.
        var stale = await owner.Client.PutAsJsonAsync($"{Zones}/{id}", Zone("Centro", 30m, "72000") with { Revision = 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(ApiErrorCodes.ConcurrencyConflict, JsonDocument.Parse(await stale.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());

        var taken = await owner.Client.PostAsJsonAsync(Zones, Zone("Norte", 45m, "72005"));
        Assert.Equal(HttpStatusCode.BadRequest, taken.StatusCode);
        Assert.Equal(DeliveryZoneErrors.PostalCodeInAnotherZone("72005", "Centro"),
            JsonDocument.Parse(await taken.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString());

        var notAPostalCode = await owner.Client.PostAsJsonAsync(Zones, Zone("Sur", 45m, "7200"));
        Assert.Equal(HttpStatusCode.BadRequest, notAPostalCode.StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.DeleteAsync($"{Zones}/{id}?revision=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync($"{Zones}/{id}?revision=2")).StatusCode);
        Assert.Empty((await owner.Client.GetFromJsonAsync<JsonElement>(Zones)).EnumerateArray());
        Assert.Equal(
            ["DeliveryZoneCreated", "DeliveryZoneUpdated", "DeliveryZoneDeleted"],
            await _api.QueryAsync(owner.TenantId, db => db.AuditLogs
                .Where(log => log.EntityName == "DeliveryZone")
                .OrderBy(log => log.Timestamp)
                .Select(log => log.Action)
                .ToListAsync()));
    }

    [SqlServerFact]
    public async Task Zones_ShouldBeReadByTheTeam_AndChangedOnlyByOwnersAndAdmins()
    {
        var owner = await _api.SignUpAsync("zonas-equipo", TwoBusinessesApi.FoodBusiness);
        var zone = await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync(Zones, Zone("Centro", 35m, "72000")));
        var id = zone.GetProperty("id").GetGuid();
        var cashier = await EmployeeAsync(owner, appRole: 3, "ManageOrders");

        Assert.Single((await cashier.GetFromJsonAsync<JsonElement>(Zones)).EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync(Zones, Zone("Norte", 1m, "72100"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PutAsJsonAsync($"{Zones}/{id}", Zone("Centro", 1m, "72000") with { Revision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.DeleteAsync($"{Zones}/{id}?revision=1")).StatusCode);
        Assert.Equal(35m, await _api.QueryAsync(owner.TenantId, db => db.DeliveryZones.Select(z => z.Fee).SingleAsync()));
    }

    private static ZoneBody Zone(string name, decimal fee, params string[] postalCodes)
        => new(name, fee, 0m, null, postalCodes, true, 0);

    private sealed record ZoneBody(
        string Name, decimal Fee, decimal MinimumOrder, decimal? FreeDeliveryFrom, string[] PostalCodes, bool IsActive, int Revision);

    private async Task<HttpClient> EmployeeAsync(SignedUp owner, int appRole, params string[] permissions)
    {
        var email = $"equipo-{Guid.NewGuid():N}@demo.com";
        await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync("/api/v1/employees", new
        {
            email,
            password = Password,
            firstName = "Empleado",
            lastName = "De prueba",
            appRole,
            tenantRole = 2,
            permissions
        }));
        var client = _api.AnonymousClient();
        var signedIn = await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.GetProperty("accessToken").GetString());
        return client;
    }
}
