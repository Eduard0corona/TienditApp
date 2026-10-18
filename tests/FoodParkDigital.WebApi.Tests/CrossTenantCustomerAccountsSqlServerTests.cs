using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.Customers;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Buying with a customer account (Fase 24B) through the API: each account keeps and sees only its own addresses, an
/// order placed with its session is in its orders and in no other account's, the key of an order places it once in its
/// business, and Explorar says with the customer's postal code who delivers there and what it costs, never the zone.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantCustomerAccountsSqlServerTests
{
    private const string Addresses = "/api/v1/me/addresses";
    private const string Favorites = "/api/v1/me/favorites";
    private const string OnlineMenu = "/api/v1/auth/my-tenant/online-menu";
    private const string Address = "Av. Reforma 123, Centro";

    private readonly TwoBusinessesApi _api;

    public CrossTenantCustomerAccountsSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Addresses_ShouldBeOnlyTheAccounts()
    {
        var ana = await _api.SignUpCustomerAsync("direcciones-a");
        var beto = await _api.SignUpCustomerAsync("direcciones-b");
        var owner = await _api.SignUpAsync("direcciones-dueno");

        var casa = await TwoBusinessesApi.ReadAsync(await ana.Client.PostAsJsonAsync(Addresses,
            new { label = "Casa", postalCode = "72000", addressLine = Address, references = "Portón verde" }));
        var id = casa.GetProperty("id").GetGuid();
        var listed = await TwoBusinessesApi.ReadAsync(await ana.Client.GetAsync(Addresses));
        Assert.Equal(id, Assert.Single(listed.EnumerateArray()).GetProperty("id").GetGuid());

        // Another account neither reads, changes nor deletes it.
        Assert.Empty((await TwoBusinessesApi.ReadAsync(await beto.Client.GetAsync(Addresses))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await beto.Client.PutAsJsonAsync($"{Addresses}/{id}",
            new { label = "Mía", postalCode = "72000", addressLine = "Calle de Beto 1" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await beto.Client.DeleteAsync($"{Addresses}/{id}")).StatusCode);
        // The team of a business keeps none; without a session there is no account.
        var byTheTeam = await owner.Client.PostAsJsonAsync(Addresses, new { postalCode = "72000", addressLine = Address });
        Assert.Equal(HttpStatusCode.BadRequest, byTheTeam.StatusCode);
        Assert.Equal(AccountAddressRules.CustomersOnlyMessage, await DetailAsync(byTheTeam));
        using var anonymous = _api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Addresses)).StatusCode);

        var kept = await _api.QueryAsync(Guid.Empty, db => db.AccountAddresses.ToListAsync());
        var stored = Assert.Single(kept, a => a.Id == id);
        Assert.Equal((ana.UserId, "Casa", Address), (stored.UserId, stored.Label, stored.AddressLine));
        Assert.DoesNotContain(kept, a => a.UserId == beto.UserId);

        Assert.Equal(HttpStatusCode.NoContent, (await ana.Client.DeleteAsync($"{Addresses}/{id}")).StatusCode);
        Assert.Empty((await TwoBusinessesApi.ReadAsync(await ana.Client.GetAsync(Addresses))).EnumerateArray());
    }

    [SqlServerFact]
    public async Task OrderWithASession_ShouldBeTheAccounts_AndItsKeyShouldPlaceItOnceInItsBusiness()
    {
        var mine = await _api.SignUpAsync("cuenta-menu-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("cuenta-menu-b", TwoBusinessesApi.FoodBusiness);
        var myTacos = await ProductAsync(mine.Client, "Tacos de canasta");
        var theirTacos = await ProductAsync(theirs.Client, "Tacos de cabeza");
        await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        await TwoBusinessesApi.IdAsync(theirs.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        await ZoneAsync(mine.Client, "Interna Centro", 35m, "72000");
        var mySlug = await MenuWithOrdersAsync(mine.Client);
        var theirSlug = await MenuWithOrdersAsync(theirs.Client);
        var ana = await _api.SignUpCustomerAsync("cuenta-menu-ana");
        var beto = await _api.SignUpCustomerAsync("cuenta-menu-beto");
        var key = Guid.NewGuid();

        // Ana orders with her session, to an address she keeps, with a phone she never confirmed.
        object Order(Guid product, string? postalCode = null) => new
        {
            customerName = "Ana Cliente",
            customerPhone = "5512345678",
            items = new[] { new { productId = product, quantity = 2 } },
            delivery = postalCode is null ? null : new { postalCode, addressLine = Address },
            idempotencyKey = key,
            saveAddress = true,
            addressLabel = "Casa"
        };
        var placed = await TwoBusinessesApi.ReadAsync(await ana.Client.PostAsJsonAsync($"/api/v1/public/menu/{mySlug}/orders", Order(myTacos, "72000")));
        var again = await TwoBusinessesApi.ReadAsync(await ana.Client.PostAsJsonAsync($"/api/v1/public/menu/{mySlug}/orders", Order(myTacos, "72000")));
        Assert.True(placed.GetProperty("linkedToAccount").GetBoolean());
        Assert.Equal(placed.GetProperty("orderNumber").GetString(), again.GetProperty("orderNumber").GetString());

        // The same key in another business is another order, of that business.
        var elsewhere = await TwoBusinessesApi.ReadAsync(await ana.Client.PostAsJsonAsync($"/api/v1/public/menu/{theirSlug}/orders", Order(theirTacos)));
        Assert.True(elsewhere.GetProperty("linkedToAccount").GetBoolean());
        Assert.Equal(1, await _api.QueryAsync(mine.TenantId, db => db.Orders.CountAsync(o => o.ClientRequestId == key)));
        Assert.Equal(1, await _api.QueryAsync(theirs.TenantId, db => db.Orders.CountAsync(o => o.ClientRequestId == key)));

        // Without a session the same key does not read Ana's order: it was sent already.
        using var stranger = _api.AnonymousClient();
        var replayed = await stranger.PostAsJsonAsync($"/api/v1/public/menu/{mySlug}/orders", new
        {
            customerName = "Otra persona",
            customerPhone = "5587654321",
            items = new[] { new { productId = myTacos, quantity = 1 } },
            idempotencyKey = key
        });
        Assert.Equal(HttpStatusCode.BadRequest, replayed.StatusCode);
        Assert.Equal(PlacePublicOrderCommandHandler.AlreadySentMessage, await DetailAsync(replayed));
        Assert.DoesNotContain(placed.GetProperty("orderNumber").GetString()!, await replayed.Content.ReadAsStringAsync());

        // Her orders are hers, in both businesses; Beto's account has none.
        var anas = await TwoBusinessesApi.ReadAsync(await ana.Client.GetAsync("/api/v1/me/orders?pageSize=50"));
        Assert.Equal(2, anas.GetProperty("items").GetArrayLength());
        var item = anas.GetProperty("items").EnumerateArray()
            .Single(o => o.GetProperty("businessSlug").GetString() == mySlug).GetProperty("items")[0];
        Assert.Equal(myTacos, item.GetProperty("productId").GetGuid());
        Assert.Equal(0, (await TwoBusinessesApi.ReadAsync(await beto.Client.GetAsync("/api/v1/me/orders?pageSize=50"))).GetProperty("items").GetArrayLength());
        Assert.Empty(await _api.QueryAsync(mine.TenantId, db => db.TemporaryCustomers.ToListAsync()));

        // The address went to her account, used by this order; Beto keeps none.
        var kept = await TwoBusinessesApi.ReadAsync(await ana.Client.GetAsync(Addresses));
        var casa = Assert.Single(kept.EnumerateArray());
        Assert.Equal(("Casa", "72000", Address), (casa.GetProperty("label").GetString(), casa.GetProperty("postalCode").GetString(), casa.GetProperty("addressLine").GetString()));
        Assert.Equal(JsonValueKind.String, casa.GetProperty("lastUsedAt").ValueKind);
        Assert.Empty((await TwoBusinessesApi.ReadAsync(await beto.Client.GetAsync(Addresses))).EnumerateArray());
    }

    [SqlServerFact]
    public async Task Explorar_ShouldSayWhoDeliversToThePostalCode_WithoutTheZone()
    {
        var reaches = await _api.SignUpAsync("cp-explorar-a", TwoBusinessesApi.FoodBusiness);
        var other = await _api.SignUpAsync("cp-explorar-b", TwoBusinessesApi.FoodBusiness);
        foreach (var owner in new[] { reaches, other })
        {
            await ProductAsync(owner.Client, "Tlayudas");
            await TwoBusinessesApi.IdAsync(owner.Client, "/api/v1/cash/open", new { openingAmount = 100m });
            await MenuWithOrdersAsync(owner.Client);
        }
        await ZoneAsync(reaches.Client, "Ruta interna 7", 30m, "72000");
        await ZoneAsync(other.Client, "Norte", 45m, "72100");
        _api.ForgetDirectorySnapshot();
        using var customer = _api.AnonymousClient();

        var response = await customer.GetAsync("/api/v1/public/directory?q=cp-explorar&postalCode=72000");
        var body = await response.Content.ReadAsStringAsync();
        var directory = await TwoBusinessesApi.ReadAsync(response);

        Assert.Equal("72000", directory.GetProperty("postalCode").GetString());
        var cards = directory.GetProperty("businesses").GetProperty("items").EnumerateArray()
            .ToDictionary(card => card.GetProperty("name").GetString()!);
        Assert.Equal((true, 30m), (cards["Negocio CP-EXPLORAR-A"].GetProperty("deliversToPostalCode").GetBoolean(),
            cards["Negocio CP-EXPLORAR-A"].GetProperty("deliveryFee").GetDecimal()));
        Assert.False(cards["Negocio CP-EXPLORAR-B"].GetProperty("deliversToPostalCode").GetBoolean());
        Assert.DoesNotContain("Ruta interna", body);
        Assert.DoesNotContain("Norte", body);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.GetAsync("/api/v1/public/directory?postalCode=7200")).StatusCode);
    }

    [SqlServerFact]
    public async Task FavoritesAndProducts_ShouldBeOnlyTheAccounts_AndOnlyOfPublishedMenus()
    {
        var published = await _api.SignUpAsync("favoritos-a", TwoBusinessesApi.FoodBusiness);
        var unpublished = await _api.SignUpAsync("favoritos-b", TwoBusinessesApi.FoodBusiness);
        await ProductAsync(published.Client, "Tlayuda favoritos24f de tasajo");
        await ProductAsync(unpublished.Client, "Tlayuda favoritos24f de cecina");
        var slug = await MenuWithOrdersAsync(published.Client);
        var hiddenSlug = (await TwoBusinessesApi.ReadAsync(await unpublished.Client.PutAsJsonAsync(OnlineMenu, new { mode = 0 })))
            .GetProperty("slug").GetString()!;
        _api.ForgetDirectorySnapshot();
        var ana = await _api.SignUpCustomerAsync("favoritos-ana");
        var beto = await _api.SignUpCustomerAsync("favoritos-beto");
        using var anonymous = _api.AnonymousClient();

        // A search finds the products of the published menu, never those of a menu nobody outside the business sees.
        var found = await TwoBusinessesApi.ReadAsync(await anonymous.GetAsync("/api/v1/public/directory?q=favoritos24f"));
        var product = Assert.Single(found.GetProperty("products").EnumerateArray());
        Assert.Equal(("Tlayuda favoritos24f de tasajo", slug),
            (product.GetProperty("name").GetString(), product.GetProperty("businessSlug").GetString()));

        // Ana keeps the published store; one not published is not found.
        Assert.Equal(HttpStatusCode.NoContent, (await ana.Client.PutAsync($"{Favorites}/{slug}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.Client.PutAsync($"{Favorites}/{hiddenSlug}", null)).StatusCode);
        var anas = await TwoBusinessesApi.ReadAsync(await ana.Client.GetAsync(Favorites));
        var favorite = Assert.Single(anas.EnumerateArray());
        Assert.Equal(slug, favorite.GetProperty("slug").GetString());
        Assert.Equal(slug, favorite.GetProperty("card").GetProperty("slug").GetString());

        // Beto neither sees nor takes out Ana's; the team of a business keeps none; without a session there is no account.
        Assert.Empty((await TwoBusinessesApi.ReadAsync(await beto.Client.GetAsync(Favorites))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await beto.Client.DeleteAsync($"{Favorites}/{slug}")).StatusCode);
        Assert.Single((await TwoBusinessesApi.ReadAsync(await ana.Client.GetAsync(Favorites))).EnumerateArray());
        var byTheTeam = await published.Client.PutAsync($"{Favorites}/{slug}", null);
        Assert.Equal(HttpStatusCode.BadRequest, byTheTeam.StatusCode);
        Assert.Equal(FavoriteBusinessRules.CustomersOnlyMessage, await DetailAsync(byTheTeam));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Favorites)).StatusCode);
        var kept = await _api.QueryAsync(Guid.Empty, db => db.FavoriteBusinesses.ToListAsync());
        Assert.Equal(published.TenantId, Assert.Single(kept, f => f.UserId == ana.UserId).BusinessId);
        Assert.DoesNotContain(kept, f => f.UserId == beto.UserId);

        Assert.Equal(HttpStatusCode.NoContent, (await ana.Client.DeleteAsync($"{Favorites}/{slug}")).StatusCode);
        Assert.Empty((await TwoBusinessesApi.ReadAsync(await ana.Client.GetAsync(Favorites))).EnumerateArray());
    }

    private static async Task ZoneAsync(HttpClient owner, string name, decimal fee, params string[] postalCodes)
        => await TwoBusinessesApi.ReadAsync(await owner.PostAsJsonAsync("/api/v1/delivery-zones", new
        {
            name,
            fee,
            minimumOrder = 0m,
            freeDeliveryFrom = (decimal?)null,
            postalCodes,
            isActive = true,
            revision = 0
        }));

    /// <returns>The slug of the menu, now taking orders.</returns>
    private static async Task<string> MenuWithOrdersAsync(HttpClient owner)
        => (await TwoBusinessesApi.ReadAsync(await owner.PutAsJsonAsync(OnlineMenu, new { mode = 2 }))).GetProperty("slug").GetString()!;

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString();
}
