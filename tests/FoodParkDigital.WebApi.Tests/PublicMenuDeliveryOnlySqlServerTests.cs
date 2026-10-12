using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Infrastructure.Tests.Support;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// A kitchen without premises on the real API (the whole pipeline in process, SQL Server): once its main branch only
/// delivers, the public menu says so and its answer carries no street at all; a save from a screen that does not know the
/// field keeps it private; and changed back to premises, the address is public again. Runs where TIENDITAPP_TEST_SQLSERVER
/// names a server (the Migrations job of CI).
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class PublicMenuDeliveryOnlySqlServerTests
{
    private const string OnlineMenu = "/api/v1/auth/my-tenant/online-menu";
    private const string Street = "Privada Los Pinos 8";
    private const int Orders = 2;
    private const int Premises = 0;
    private const int DeliveryOnly = 1;

    private readonly TwoBusinessesApi _api;

    public PublicMenuDeliveryOnlySqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task MainBranchThatOnlyDelivers_ShouldNeverReachTheMenu_UntilItHasPremisesAgain()
    {
        var owner = await _api.SignUpAsync("cocina-sin-local", TwoBusinessesApi.FoodBusiness);
        await TwoBusinessesApi.IdAsync(owner.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        var slug = (await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync(OnlineMenu, new { mode = Orders })))
            .GetProperty("slug").GetString()!;
        var main = (await owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/branches"))
            .EnumerateArray().Single(b => b.GetProperty("isDefault").GetBoolean());
        var path = $"/api/v1/branches/{main.GetProperty("id").GetGuid()}";
        object Fields(int? serviceMode) => new
        {
            id = main.GetProperty("id").GetGuid(),
            name = "Cocina",
            code = main.GetProperty("code").GetString(),
            addressLine1 = Street,
            addressLine2 = "Jardines",
            city = "Puebla",
            postalCode = "72000",
            serviceMode
        };
        using var customer = _api.AnonymousClient();

        var saved = await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync(path, Fields(DeliveryOnly)));
        Assert.Equal(DeliveryOnly, saved.GetProperty("serviceMode").GetInt32());
        // The team still sees the address: it is the customers who do not.
        Assert.Equal(Street, saved.GetProperty("addressLine1").GetString());
        var (menu, body) = await MenuAsync(customer, slug);
        Assert.True(menu.GetProperty("deliveryOnly").GetBoolean());
        Assert.Equal(JsonValueKind.Null, menu.GetProperty("address").ValueKind);
        Assert.True(menu.GetProperty("acceptsOrders").GetBoolean());
        Assert.DoesNotContain("Los Pinos", body);
        Assert.DoesNotContain("72000", body);

        // A tab opened before the field existed saves without it: the branch keeps delivering only.
        await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync(path, Fields(serviceMode: null)));
        (menu, body) = await MenuAsync(customer, slug);
        Assert.True(menu.GetProperty("deliveryOnly").GetBoolean());
        Assert.DoesNotContain("Los Pinos", body);

        await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync(path, Fields(Premises)));
        (menu, _) = await MenuAsync(customer, slug);
        Assert.False(menu.GetProperty("deliveryOnly").GetBoolean());
        Assert.Equal($"{Street}, Jardines, 72000 Puebla", menu.GetProperty("address").GetString());
    }

    private static async Task<(JsonElement Menu, string Body)> MenuAsync(HttpClient customer, string slug)
    {
        var response = await customer.GetAsync($"/api/v1/public/menu/{slug}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return (JsonDocument.Parse(body).RootElement.Clone(), body);
    }
}
