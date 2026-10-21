using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Conteo físico (Fase 27A, ADR 0037) through the API: each business counts only its own locations and items, another
/// business can neither see nor touch its counts, posting moves only its stock, and counting needs ManageInventory.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantInventoryCountsSqlServerTests
{
    private const string Counts = "/api/v1/inventory/counts";
    private const string Password = "Secreto1";

    private readonly TwoBusinessesApi _api;

    public CrossTenantInventoryCountsSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Counts_ShouldBeOnlyTheBusinesses_AndPostingShouldMoveOnlyItsStock()
    {
        var mine = await _api.SignUpAsync("conteo-a");
        var theirs = await _api.SignUpAsync("conteo-b");
        var (myLocation, mySupply) = await StockAsync(mine.Client, "Harina de A");
        var (theirLocation, theirSupply) = await StockAsync(theirs.Client, "Harina de B");

        // Another business cannot count my location or my items.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await theirs.Client.PostAsJsonAsync(Counts, new { inventoryLocationId = myLocation })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await theirs.Client.PostAsJsonAsync(Counts, new { inventoryLocationId = theirLocation, supplyItemIds = new[] { mySupply } })).StatusCode);

        var started = await mine.Client.PostAsJsonAsync(Counts, new { inventoryLocationId = myLocation });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var myCount = (await TwoBusinessesApi.ReadAsync(started)).GetProperty("id").GetGuid();
        var myLine = (await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"{Counts}/{myCount}/lines")))
            .GetProperty("items")[0].GetProperty("id").GetGuid();
        var theirCount = await TwoBusinessesApi.IdAsync(theirs.Client, Counts, new { inventoryLocationId = theirLocation });

        // My count is invisible and untouchable for them.
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync($"{Counts}/{myCount}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync($"{Counts}/{myCount}/lines")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PutAsJsonAsync($"{Counts}/{myCount}/lines",
            new { lines = new[] { new { lineId = myLine, countedQuantity = 1m } } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsJsonAsync($"{Counts}/{myCount}/items",
            new { supplyItemIds = new[] { theirSupply } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsJsonAsync($"{Counts}/{myCount}/post", new { expectedRevision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsJsonAsync($"{Counts}/{myCount}/cancel", new { })).StatusCode);
        var theirList = await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync($"{Counts}?pageSize=100"));
        Assert.Equal([theirCount], theirList.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()));
        // Nor can they slip my line into their count, or I their item into mine.
        Assert.Equal(HttpStatusCode.BadRequest, (await theirs.Client.PutAsJsonAsync($"{Counts}/{theirCount}/lines",
            new { lines = new[] { new { lineId = myLine, countedQuantity = 1m } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await mine.Client.PostAsJsonAsync($"{Counts}/{myCount}/items",
            new { supplyItemIds = new[] { theirSupply } })).StatusCode);

        // I count 8 of my 10 and post: only my stock moves.
        var recorded = await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync($"{Counts}/{myCount}/lines",
            new { lines = new[] { new { lineId = myLine, countedQuantity = 8m } } }));
        var revision = recorded.GetProperty("count").GetProperty("revision").GetInt32();
        // The page reviewed carries the revision it was read at; an older one is a conflict to review again.
        var review = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"{Counts}/{myCount}/lines?withDifference=true"));
        Assert.Equal(revision, review.GetProperty("revision").GetInt32());
        var stale = await mine.Client.PostAsJsonAsync($"{Counts}/{myCount}/post", new { expectedRevision = revision - 1 });
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (stale.StatusCode, await CodeAsync(stale)));
        var posted = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync($"{Counts}/{myCount}/post", new { expectedRevision = revision }));
        Assert.Equal(2, posted.GetProperty("status").GetInt32());
        // Posted, it no longer changes: counting it again is a conflict too.
        var closed = await mine.Client.PutAsJsonAsync($"{Counts}/{myCount}/lines", new { lines = new[] { new { lineId = myLine, countedQuantity = 7m } } });
        Assert.Equal((HttpStatusCode.Conflict, "conflict"), (closed.StatusCode, await CodeAsync(closed)));
        Assert.Equal(8m, await _api.QueryAsync(mine.TenantId, db => db.InventoryStocks.Where(s => s.SupplyItemId == mySupply).Select(s => s.OnHandQuantity).SingleAsync()));
        Assert.Equal(10m, await _api.QueryAsync(theirs.TenantId, db => db.InventoryStocks.Where(s => s.SupplyItemId == theirSupply).Select(s => s.OnHandQuantity).SingleAsync()));
        Assert.Equal(0, await _api.QueryAsync(theirs.TenantId, db => db.InventoryMovements.CountAsync(m => m.ExternalReference!.StartsWith("COUNT:"))));
    }

    [SqlServerFact]
    public async Task Counting_ShouldNeedManageInventory()
    {
        var owner = await _api.SignUpAsync("conteo-permisos");
        var (location, _) = await StockAsync(owner.Client, "Azúcar");
        var cashier = await EmployeeAsync(owner, "ChargeOrders", "ManageOrders");
        var stockKeeper = await EmployeeAsync(owner, "ManageInventory");

        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(Counts)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync(Counts, new { inventoryLocationId = location })).StatusCode);
        var started = await stockKeeper.PostAsJsonAsync(Counts, new { inventoryLocationId = location });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var count = (await TwoBusinessesApi.ReadAsync(started)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync($"{Counts}/{count}/cancel", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stockKeeper.PostAsJsonAsync($"{Counts}/{count}/cancel", new { reason = "Prueba" })).StatusCode);
    }

    /// <summary>The business's warehouse with 10 of a new item at $10.</summary>
    private static async Task<(Guid Location, Guid Supply)> StockAsync(HttpClient client, string name)
    {
        var locations = await TwoBusinessesApi.ReadAsync(await client.GetAsync("/api/v1/inventory/locations"));
        var location = locations.EnumerateArray().Single(l => l.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid();
        var supply = await TwoBusinessesApi.IdAsync(client, "/api/v1/inventory/supplies", new { name, unit = 0, defaultCost = 10m });
        await TwoBusinessesApi.IdAsync(client, "/api/v1/inventory/movements", new
        {
            movementType = 1,
            movementReason = 1,
            lines = new[] { new { supplyItemId = supply, inventoryLocationId = location, quantity = 10m, unitCost = 10m } }
        });
        return (location, supply);
    }

    /// <summary>A member of the owner's team with the permissions given, signed in with its own password.</summary>
    private async Task<HttpClient> EmployeeAsync(SignedUp owner, params string[] permissions)
    {
        var email = $"equipo-{Guid.NewGuid():N}@demo.com";
        await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync("/api/v1/employees", new
        {
            email,
            password = Password,
            firstName = "Empleado",
            lastName = "De prueba",
            appRole = 3,
            tenantRole = 2,
            permissions
        }));
        var client = _api.AnonymousClient();
        var signedIn = await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("code").GetString();
    }
}
