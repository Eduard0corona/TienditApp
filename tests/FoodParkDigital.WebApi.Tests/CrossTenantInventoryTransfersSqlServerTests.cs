using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Transferencias entre ubicaciones (Fase 27B, ADR 0038) through the API: each business transfers only between its own
/// locations and with its own items, another business can neither see its transfers nor move its stock, and transferring
/// needs ManageInventory. Manual movements no longer make one leg of a transfer.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantInventoryTransfersSqlServerTests
{
    private const string Transfers = "/api/v1/inventory/transfers";
    private const string Password = "Secreto1";

    private readonly TwoBusinessesApi _api;

    public CrossTenantInventoryTransfersSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Transfers_ShouldBeOnlyTheBusinesses_AndMoveOnlyItsStock()
    {
        var mine = await _api.SignUpAsync("transfer-a");
        var theirs = await _api.SignUpAsync("transfer-b");
        var (myWarehouse, myStore, mySupply) = await ShelfAsync(mine.Client, "Harina de A");
        var (theirWarehouse, theirStore, theirSupply) = await ShelfAsync(theirs.Client, "Harina de B");

        // Another business cannot transfer from or to my locations, nor my items between its own.
        Assert.Equal(HttpStatusCode.BadRequest, (await theirs.Client.PostAsJsonAsync(Transfers, Transfer(myWarehouse, theirStore, theirSupply))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await theirs.Client.PostAsJsonAsync(Transfers, Transfer(theirWarehouse, myStore, theirSupply))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await theirs.Client.PostAsJsonAsync(Transfers, Transfer(theirWarehouse, theirStore, mySupply))).StatusCode);

        var request = Transfer(myWarehouse, myStore, mySupply, 4m);
        var made = await mine.Client.PostAsJsonAsync(Transfers, request);
        Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        var myTransfer = (await TwoBusinessesApi.ReadAsync(made)).GetProperty("id").GetGuid();
        // The same key again is the same transfer; mine, even if they send it.
        Assert.Equal(myTransfer, (await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(Transfers, request))).GetProperty("id").GetGuid());
        var theirTransfer = await TwoBusinessesApi.IdAsync(theirs.Client, Transfers, Transfer(theirWarehouse, theirStore, theirSupply, 1m));

        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync($"{Transfers}/{myTransfer}")).StatusCode);
        var theirList = await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync($"{Transfers}?pageSize=100"));
        Assert.Equal([theirTransfer], theirList.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()));
        Assert.Equal(0, (await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync($"{Transfers}?inventoryLocationId={myWarehouse}")))
            .GetProperty("totalCount").GetInt32());

        Assert.Equal([4m, 6m], await _api.QueryAsync(mine.TenantId, async db =>
            (await db.InventoryStocks.Where(s => s.SupplyItemId == mySupply).Select(s => s.OnHandQuantity).ToListAsync()).Order().ToList()));
        Assert.Equal([1m, 9m], await _api.QueryAsync(theirs.TenantId, async db =>
            (await db.InventoryStocks.Where(s => s.SupplyItemId == theirSupply).Select(s => s.OnHandQuantity).ToListAsync()).Order().ToList()));
        Assert.Equal(1, await _api.QueryAsync(theirs.TenantId, db => db.InventoryTransfers.CountAsync()));
    }

    [SqlServerFact]
    public async Task Transferring_ShouldNeedManageInventory_AndManualMovementsShouldNotBeALegOfOne()
    {
        var owner = await _api.SignUpAsync("transfer-permisos");
        var (warehouse, store, supply) = await ShelfAsync(owner.Client, "Azúcar");
        var cashier = await EmployeeAsync(owner, "ChargeOrders", "ManageOrders");
        var stockKeeper = await EmployeeAsync(owner, "ManageInventory");

        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(Transfers)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync(Transfers, Transfer(warehouse, store, supply))).StatusCode);
        var made = await stockKeeper.PostAsJsonAsync(Transfers, Transfer(warehouse, store, supply));
        Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cashier.GetAsync($"{Transfers}/{(await TwoBusinessesApi.ReadAsync(made)).GetProperty("id").GetGuid()}")).StatusCode);

        // Leaving one location and entering another happens only as a transfer.
        var leg = await owner.Client.PostAsJsonAsync("/api/v1/inventory/movements", new
        {
            movementType = 6,
            movementReason = 99,
            lines = new[] { new { supplyItemId = supply, inventoryLocationId = warehouse, quantity = 1m } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, leg.StatusCode);
        // More than there is: nothing moves, and the answer names what there is.
        var tooMuch = await owner.Client.PostAsJsonAsync(Transfers, Transfer(warehouse, store, supply, 50m));
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        using var problem = JsonDocument.Parse(await tooMuch.Content.ReadAsStringAsync());
        Assert.Contains("(hay 9)", problem.RootElement.GetProperty("detail").GetString());
    }

    private static object Transfer(Guid from, Guid to, Guid supply, decimal quantity = 1m) => new
    {
        idempotencyKey = Guid.NewGuid(),
        fromLocationId = from,
        toLocationId = to,
        lines = new[] { new { supplyItemId = supply, quantity } }
    };

    /// <summary>The business's warehouse with 10 of a new item at $10, and a second location.</summary>
    private static async Task<(Guid Warehouse, Guid Store, Guid Supply)> ShelfAsync(HttpClient client, string name)
    {
        var locations = await TwoBusinessesApi.ReadAsync(await client.GetAsync("/api/v1/inventory/locations"));
        var warehouse = locations.EnumerateArray().Single(l => l.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid();
        var store = await TwoBusinessesApi.IdAsync(client, "/api/v1/inventory/locations", new { name = $"Sucursal {name}", code = $"S{Guid.NewGuid():N}"[..8] });
        var supply = await TwoBusinessesApi.IdAsync(client, "/api/v1/inventory/supplies", new { name, unit = 0, defaultCost = 10m });
        await TwoBusinessesApi.IdAsync(client, "/api/v1/inventory/movements", new
        {
            movementType = 1,
            movementReason = 1,
            lines = new[] { new { supplyItemId = supply, inventoryLocationId = warehouse, quantity = 10m, unitCost = 10m } }
        });
        return (warehouse, store, supply);
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
}
