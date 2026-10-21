using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Revisar que cuadre (Fase 27C, ADR 0039) through the API: each business sees and reconciles only its own stocks,
/// another business can neither read why one of them does not add up nor record anything in its history, and both need
/// ManageInventory. Manual movements cannot take the reason of a reconciliation.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantInventoryReconciliationSqlServerTests
{
    private const string Reconciliation = "/api/v1/inventory/reconciliation";
    private const string Password = "Secreto1";

    private readonly TwoBusinessesApi _api;

    public CrossTenantInventoryReconciliationSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Reconciliation_ShouldBeOnlyTheBusinesses_AndRecordOnlyInItsHistory()
    {
        var mine = await _api.SignUpAsync("cuadre-a");
        var theirs = await _api.SignUpAsync("cuadre-b");
        var (myWarehouse, mySupply) = await ShelfAsync(mine.Client, "Harina de A");
        var (theirWarehouse, theirSupply) = await ShelfAsync(theirs.Client, "Harina de B");
        // Something touched each stock without a movement.
        await SetStockAsync(mine.TenantId, mySupply, 12m);
        await SetStockAsync(theirs.TenantId, theirSupply, 7m);

        var theirReview = await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync(Reconciliation));
        var theirItems = theirReview.GetProperty("mismatches").GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([(theirSupply, -3m)], theirItems.Select(i => (i.GetProperty("supplyItemId").GetGuid(), i.GetProperty("difference").GetDecimal())));
        var filteredByMine = await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync($"{Reconciliation}?inventoryLocationId={myWarehouse}"));
        Assert.Equal((0, 0), (filteredByMine.GetProperty("checkedCount").GetInt32(), filteredByMine.GetProperty("mismatches").GetProperty("totalCount").GetInt32()));
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync($"{Reconciliation}/{mySupply}/{myWarehouse}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync($"{Reconciliation}/{theirSupply}/{myWarehouse}")).StatusCode);

        // Another business records nothing in my history: neither in my location nor with my item.
        Assert.Equal(HttpStatusCode.BadRequest, (await theirs.Client.PostAsJsonAsync(Reconciliation, Reconcile(myWarehouse, mySupply, 2m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await theirs.Client.PostAsJsonAsync(Reconciliation, Reconcile(theirWarehouse, mySupply, 2m))).StatusCode);

        var detail = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"{Reconciliation}/{mySupply}/{myWarehouse}"));
        Assert.Equal((12m, 10m, 2m), (detail.GetProperty("onHandQuantity").GetDecimal(), detail.GetProperty("historyQuantity").GetDecimal(),
            detail.GetProperty("difference").GetDecimal()));
        var reconciled = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(Reconciliation, Reconcile(myWarehouse, mySupply, 2m)));
        Assert.Equal((1, 0, 1), (reconciled.GetProperty("reconciledCount").GetInt32(), reconciled.GetProperty("alreadyAddedUpCount").GetInt32(),
            reconciled.GetProperty("movements").GetArrayLength()));
        // Sent again it records nothing twice.
        var again = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(Reconciliation, Reconcile(myWarehouse, mySupply, 2m)));
        Assert.Equal((0, 1), (again.GetProperty("reconciledCount").GetInt32(), again.GetProperty("alreadyAddedUpCount").GetInt32()));
        Assert.Equal(0, (await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync(Reconciliation))).GetProperty("mismatches").GetProperty("totalCount").GetInt32());

        // The stocks did not move, and their history did not get mine.
        Assert.Equal(12m, await _api.QueryAsync(mine.TenantId, db => db.InventoryStocks.Where(s => s.SupplyItemId == mySupply).Select(s => s.OnHandQuantity).SingleAsync()));
        Assert.Equal(7m, await _api.QueryAsync(theirs.TenantId, db => db.InventoryStocks.Where(s => s.SupplyItemId == theirSupply).Select(s => s.OnHandQuantity).SingleAsync()));
        Assert.Equal(1, (await TwoBusinessesApi.ReadAsync(await theirs.Client.GetAsync(Reconciliation))).GetProperty("mismatches").GetProperty("totalCount").GetInt32());
        Assert.Equal(0, await _api.QueryAsync(theirs.TenantId, db => db.InventoryMovements.CountAsync(m => (int)m.MovementReason == 12)));
    }

    [SqlServerFact]
    public async Task Reconciling_ShouldNeedManageInventory_AndManualMovementsShouldNotTakeItsReason()
    {
        var owner = await _api.SignUpAsync("cuadre-permisos");
        var (warehouse, supply) = await ShelfAsync(owner.Client, "Azúcar");
        await SetStockAsync(owner.TenantId, supply, 8m);
        var cashier = await EmployeeAsync(owner, "ChargeOrders", "ManageOrders");
        var stockKeeper = await EmployeeAsync(owner, "ManageInventory");

        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(Reconciliation)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync($"{Reconciliation}/{supply}/{warehouse}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync(Reconciliation, Reconcile(warehouse, supply, -2m))).StatusCode);

        // What was reviewed is not what there is now: nothing is recorded and the client reviews again.
        Assert.Equal(HttpStatusCode.Conflict, (await stockKeeper.PostAsJsonAsync(Reconciliation, Reconcile(warehouse, supply, -1m))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stockKeeper.PostAsJsonAsync(Reconciliation, Reconcile(warehouse, supply, -2m))).StatusCode);

        var manual = await owner.Client.PostAsJsonAsync("/api/v1/inventory/movements", new
        {
            movementType = 3,
            movementReason = 12,
            lines = new[] { new { supplyItemId = supply, inventoryLocationId = warehouse, quantity = 1m } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, manual.StatusCode);
        Assert.Equal(8m, await _api.QueryAsync(owner.TenantId, db => db.InventoryStocks.Where(s => s.SupplyItemId == supply).Select(s => s.OnHandQuantity).SingleAsync()));
    }

    private static object Reconcile(Guid location, Guid supply, decimal expectedDifference) => new
    {
        inventoryLocationId = location,
        lines = new[] { new { supplyItemId = supply, expectedDifference } }
    };

    private Task SetStockAsync(Guid tenantId, Guid supply, decimal onHand)
        => _api.QueryAsync(tenantId, db => db.InventoryStocks
            .Where(s => s.SupplyItemId == supply)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.OnHandQuantity, onHand)));

    /// <summary>The business's warehouse with 10 of a new item at $10.</summary>
    private static async Task<(Guid Warehouse, Guid Supply)> ShelfAsync(HttpClient client, string name)
    {
        var locations = await TwoBusinessesApi.ReadAsync(await client.GetAsync("/api/v1/inventory/locations"));
        var warehouse = locations.EnumerateArray().Single(l => l.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid();
        var supply = await TwoBusinessesApi.IdAsync(client, "/api/v1/inventory/supplies", new { name, unit = 0, defaultCost = 10m });
        await TwoBusinessesApi.IdAsync(client, "/api/v1/inventory/movements", new
        {
            movementType = 1,
            movementReason = 1,
            lines = new[] { new { supplyItemId = supply, inventoryLocationId = warehouse, quantity = 10m, unitCost = 10m } }
        });
        return (warehouse, supply);
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
