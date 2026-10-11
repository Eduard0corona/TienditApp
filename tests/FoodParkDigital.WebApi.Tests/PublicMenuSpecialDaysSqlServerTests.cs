using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// A closed special day on the real API (the whole pipeline in process, SQL Server): the public menu of the business
/// says it is closed and why, its orders are refused with the same words, from one of its tables too, and the menu of
/// another business is not affected. Runs where TIENDITAPP_TEST_SQLSERVER names a server (the Migrations job of CI).
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class PublicMenuSpecialDaysSqlServerTests
{
    private const string SpecialDays = "/api/v1/auth/my-tenant/special-days";
    private const string OnlineMenu = "/api/v1/auth/my-tenant/online-menu";
    private const int Orders = 2;

    private readonly TwoBusinessesApi _api;

    public PublicMenuSpecialDaysSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task ClosedSpecialDay_ShouldPauseTheMenu_AndRefuseItsOrders_FromATableToo()
    {
        // Tables are for food businesses: one of its own, without weekly hours, closed today for inventory.
        var owner = await _api.SignUpAsync("menu-cerrado", TwoBusinessesApi.FoodBusiness);
        var product = await ProductAsync(owner.Client, "Café de olla");
        await TwoBusinessesApi.IdAsync(owner.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        await TwoBusinessesApi.IdAsync(owner.Client, "/api/v1/tables", new { name = "Mesa 1", seats = 4, sortOrder = 1 });
        var slug = await MenuWithOrdersAsync(owner.Client);
        var today = (await owner.Client.GetFromJsonAsync<JsonElement>(SpecialDays)).GetProperty("today").GetString();
        await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync(SpecialDays, new { from = today, kind = 0, reason = "Inventario" }));

        var bBefore = await _api.B.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/my-tenant");
        using var customer = _api.AnonymousClient();
        try
        {
            foreach (var table in new string?[] { null, "Mesa 1" })
            {
                var query = table is null ? "" : $"?table={Uri.EscapeDataString(table)}";
                var menu = await TwoBusinessesApi.ReadAsync(await customer.GetAsync($"/api/v1/public/menu/{slug}{query}"));
                Assert.False(menu.GetProperty("acceptsOrders").GetBoolean(), table);
                Assert.True(menu.GetProperty("ordersPaused").GetBoolean(), table);
                var hours = menu.GetProperty("openingHours");
                Assert.True(hours.GetProperty("closedBySpecialDay").GetBoolean(), table);
                Assert.Equal(today, hours.GetProperty("todayDate").GetString());
                Assert.Equal("Cerrado", hours.GetProperty("statusLabel").GetString());
                Assert.Equal("por Inventario · abre mañana", hours.GetProperty("statusDetail").GetString());
                Assert.Equal(
                    "No recibimos pedidos en línea por Inventario: vuelve mañana. Mientras, puedes ver el menú.",
                    hours.GetProperty("closedNotice").GetString());

                var refused = await customer.PostAsJsonAsync($"/api/v1/public/menu/{slug}/orders", new
                {
                    customerName = "Ana",
                    customerPhone = "5512345678",
                    tableLabel = table,
                    items = new[] { new { productId = product, quantity = 1 } }
                });
                Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
                var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("Está cerrado por Inventario: abre mañana.", problem.GetProperty("detail").GetString());
            }
            Assert.Equal(0, await _api.QueryAsync(owner.TenantId, db => db.Orders.CountAsync()));

            // Another business on the same day takes its orders as always.
            var bSlug = await MenuWithOrdersAsync(_api.B.Client);
            var bMenu = await TwoBusinessesApi.ReadAsync(await customer.GetAsync($"/api/v1/public/menu/{bSlug}"));
            Assert.True(bMenu.GetProperty("acceptsOrders").GetBoolean());
            Assert.Equal(JsonValueKind.Null, bMenu.GetProperty("openingHours").ValueKind);
        }
        finally
        {
            // The other tests of the suite keep B's menu as it was.
            await TwoBusinessesApi.ReadAsync(await _api.B.Client.PutAsJsonAsync(OnlineMenu,
                new { mode = bBefore.GetProperty("onlineMenuMode").GetInt32() }));
        }
    }

    /// <returns>The slug of the menu, now taking orders.</returns>
    private static async Task<string> MenuWithOrdersAsync(HttpClient owner)
        => (await TwoBusinessesApi.ReadAsync(await owner.PutAsJsonAsync(OnlineMenu, new { mode = Orders }))).GetProperty("slug").GetString()!;
}
