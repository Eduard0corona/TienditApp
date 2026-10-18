using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantHttpSqlServerTests;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Explorar (Fase 24A) through the API: a customer without a session sees each business as it decided, only with what
/// its menu already shows, and only the owners of a business change how it shows (the header of another business
/// changes nothing). «Cerca de mí» (Fase 24G) measures each business to the main branch its owner put on the map, only
/// while customers come to it, and no other business moves it.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantDirectorySqlServerTests
{
    private const string Directory = "/api/v1/auth/my-tenant/directory";
    private const string OnlineMenu = "/api/v1/auth/my-tenant/online-menu";

    private readonly TwoBusinessesApi _api;

    public CrossTenantDirectorySqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Explorar_ShouldShowEachBusinessAsItDecided_AndOnlyItsOwnersChangeIt()
    {
        var mine = await _api.SignUpAsync("explorar-a", TwoBusinessesApi.FoodBusiness);
        var hidden = await _api.SignUpAsync("explorar-b", TwoBusinessesApi.FoodBusiness);
        var empty = await _api.SignUpAsync("explorar-c", TwoBusinessesApi.FoodBusiness);
        await ProductAsync(mine.Client, "Tacos al pastor");
        await ProductAsync(hidden.Client, "Tortas ahogadas");
        await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        foreach (var owner in new[] { mine, hidden, empty })
            await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync(OnlineMenu, new { mode = 2 }));

        var described = await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync(Directory,
            new { category = 0, description = "Tacos al pastor desde 1998", listed = true }));
        Assert.Equal((int)DirectoryVisibility.Shown, described.GetProperty("visibility").GetInt32());
        await TwoBusinessesApi.ReadAsync(await hidden.Client.PutAsJsonAsync(Directory, new { category = 0, listed = false }));

        // The other business, with the header of mine, changes its own: the header of another business is ignored.
        using (var spoof = new HttpRequestMessage(HttpMethod.Put, Directory)
        {
            Content = JsonContent.Create(new { category = 9, description = "Tortas y aguas frescas", listed = false })
        })
        {
            spoof.Headers.Add("X-Tenant-Id", mine.TenantId.ToString());
            var own = await TwoBusinessesApi.ReadAsync(await hidden.Client.SendAsync(spoof));
            Assert.Equal((9, (int)DirectoryVisibility.HiddenByOwner), (own.GetProperty("category").GetInt32(), own.GetProperty("visibility").GetInt32()));
        }
        var kept = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync(Directory));
        Assert.Equal((0, "Tacos al pastor desde 1998", true),
            (kept.GetProperty("category").GetInt32(), kept.GetProperty("description").GetString(), kept.GetProperty("listed").GetBoolean()));
        Assert.Equal((int)DirectoryVisibility.NoProducts,
            (await TwoBusinessesApi.ReadAsync(await empty.Client.GetAsync(Directory))).GetProperty("visibility").GetInt32());

        // A customer without a session finds only mine, taking orders, without its contact.
        _api.ForgetDirectorySnapshot();
        using var customer = _api.AnonymousClient();
        var response = await customer.GetAsync("/api/v1/public/directory?q=explorar");
        var body = await response.Content.ReadAsStringAsync();
        var directory = await TwoBusinessesApi.ReadAsync(response);
        var card = Assert.Single(directory.GetProperty("businesses").GetProperty("items").EnumerateArray());
        Assert.Equal(("Negocio EXPLORAR-A", "Tacos al pastor desde 1998", 0, true),
            (card.GetProperty("name").GetString(), card.GetProperty("description").GetString(),
                card.GetProperty("category").GetInt32(), card.GetProperty("acceptsOrders").GetBoolean()));
        Assert.DoesNotContain(mine.Email, body);
        Assert.DoesNotContain(mine.TenantId.ToString(), body);
        Assert.False(card.TryGetProperty("contactPhone", out _));

        // The change of each owner is recorded in its own business.
        var audit = await _api.QueryAsync(mine.TenantId, db => db.AuditLogs
            .Where(a => a.Action == UpdateDirectoryProfileCommandHandler.AuditAction).ToListAsync());
        Assert.Single(audit);
        Assert.Equal(2, (await _api.QueryAsync(hidden.TenantId, db => db.AuditLogs
            .Where(a => a.Action == UpdateDirectoryProfileCommandHandler.AuditAction).ToListAsync())).Count);
    }

    [SqlServerFact]
    public async Task NearMe_ShouldMeasureEachBusinessAtTheMainBranchItsOwnerPutOnTheMap_OnlyWhileCustomersComeToIt()
    {
        var shop = await _api.SignUpAsync("cerca-a", TwoBusinessesApi.FoodBusiness);
        var kitchen = await _api.SignUpAsync("cerca-b", TwoBusinessesApi.FoodBusiness);
        await ProductAsync(shop.Client, "Tacos de canasta");
        await ProductAsync(kitchen.Client, "Pozole rojo");
        await TwoBusinessesApi.IdAsync(shop.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        var slug = (await TwoBusinessesApi.ReadAsync(await shop.Client.PutAsJsonAsync(OnlineMenu, new { mode = 2 })))
            .GetProperty("slug").GetString()!;
        await TwoBusinessesApi.ReadAsync(await kitchen.Client.PutAsJsonAsync(OnlineMenu, new { mode = 2 }));

        // The shop 300 m from the customer; the kitchen, which only delivers, 200 m.
        var shopBranch = await MainBranchAsync(shop.Client);
        var saved = await TwoBusinessesApi.ReadAsync(await shop.Client.PutAsJsonAsync($"/api/v1/branches/{shopBranch}",
            new { id = shopBranch, name = "Centro", code = "CENTRO", location = new { latitude = 19.046277m, longitude = -98.198626m } }));
        Assert.Equal((19.046277m, -98.198626m), (saved.GetProperty("latitude").GetDecimal(), saved.GetProperty("longitude").GetDecimal()));
        var kitchenBranch = await MainBranchAsync(kitchen.Client);
        await TwoBusinessesApi.ReadAsync(await kitchen.Client.PutAsJsonAsync($"/api/v1/branches/{kitchenBranch}",
            new { id = kitchenBranch, name = "Cocina", code = "COCINA", serviceMode = 1, location = new { latitude = 19.045377m, longitude = -98.198626m } }));

        // The other business cannot move the shop off its corner.
        AssertRefused(await kitchen.Client.PutAsJsonAsync($"/api/v1/branches/{shopBranch}",
            new { id = shopBranch, name = "Centro", code = "CENTRO", location = new { latitude = 20.5m, longitude = -100.4m } }));

        _api.ForgetDirectorySnapshot();
        using var customer = _api.AnonymousClient();
        var response = await customer.GetAsync("/api/v1/public/directory?q=cerca&latitude=19.043577&longitude=-98.198626");
        var raw = await response.Content.ReadAsStringAsync();
        var near = await TwoBusinessesApi.ReadAsync(response);
        var cards = near.GetProperty("businesses").GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(
            [("Negocio CERCA-A", (decimal?)0.3m), ("Negocio CERCA-B", null)],
            cards.Select(card => (card.GetProperty("name").GetString(), Distance(card))));
        // No point travels in Explorar, only how far; the kitchen's stays with its team.
        Assert.DoesNotContain("19.045377", raw);
        Assert.DoesNotContain("19.046277", raw);
        // The shop's menu opens its exact point.
        var menu = await TwoBusinessesApi.ReadAsync(await customer.GetAsync($"/api/v1/public/menu/{slug}"));
        Assert.Equal(19.046277m, menu.GetProperty("mapPoint").GetProperty("latitude").GetDecimal());

        // Half a position is refused without being written back.
        var half = await customer.GetAsync("/api/v1/public/directory?latitude=19.0436");
        Assert.Equal(HttpStatusCode.BadRequest, half.StatusCode);
        Assert.DoesNotContain("19.0436", await half.Content.ReadAsStringAsync());
    }

    private static async Task<Guid> MainBranchAsync(HttpClient owner)
        => (await owner.GetFromJsonAsync<JsonElement>("/api/v1/branches"))
            .EnumerateArray().Single(b => b.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid();

    private static decimal? Distance(JsonElement card)
        => card.GetProperty("distanceKm").ValueKind == JsonValueKind.Null ? null : card.GetProperty("distanceKm").GetDecimal();
}
