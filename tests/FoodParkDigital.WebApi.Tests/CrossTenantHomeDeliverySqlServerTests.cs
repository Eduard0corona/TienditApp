using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Orders sent to the customer's address (Fase 22C) through the API: each business sends orders only with its own zones,
/// writes down the courier's trip only on its own orders (the business header of an owner changes nothing), and the
/// public menu tells a customer what the delivery of that business costs, never the zone's name, and takes the order
/// with the fee in its total.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantHomeDeliverySqlServerTests
{
    private const string Zones = "/api/v1/delivery-zones";
    private const string OnlineMenu = "/api/v1/auth/my-tenant/online-menu";
    private const string Address = "Av. Reforma 123, Centro";

    private readonly TwoBusinessesApi _api;

    public CrossTenantHomeDeliverySqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task DeliveryOfAnotherBusiness_ShouldNotUseItsZones_NorLetItWriteDownTheTrip()
    {
        var mine = await _api.SignUpAsync("domicilio-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("domicilio-b", TwoBusinessesApi.FoodBusiness);
        var myTacos = await ProductAsync(mine.Client, "Tacos al pastor");
        var theirTacos = await ProductAsync(theirs.Client, "Tacos de suadero");
        await ZoneAsync(mine.Client, "Centro", 35m, "72000");

        var order = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync("/api/v1/orders", DeliveryOrder(myTacos, "72000")));
        var orderId = order.GetProperty("id").GetGuid();
        var delivery = order.GetProperty("delivery");
        Assert.Equal(("72000", Address, "Centro", 35m),
            (delivery.GetProperty("postalCode").GetString(), delivery.GetProperty("addressLine").GetString(),
                delivery.GetProperty("zoneName").GetString(), delivery.GetProperty("fee").GetDecimal()));
        Assert.Equal(65m, order.GetProperty("total").GetDecimal());

        // My zone reaches 72000; the other business has none there.
        var notTheirs = await theirs.Client.PostAsJsonAsync("/api/v1/orders", DeliveryOrder(theirTacos, "72000"));
        Assert.Equal(HttpStatusCode.BadRequest, notTheirs.StatusCode);
        Assert.Equal(HomeDeliveryRules.NotCoveredMessage("72000"), await DetailAsync(notTheirs));

        // Writing down the trip of my order, even with the header of my business, finds no such order.
        using var ship = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/orders/{orderId}/delivery/shipment")
        {
            Content = JsonContent.Create(new { courier = 1, folio = "PQ9999", trackingUrl = "https://paquetenvia.mx/r/PQ9999" })
        };
        ship.Headers.Add("X-Tenant-Id", mine.TenantId.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(ship)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync($"/api/v1/orders/{orderId}")).StatusCode);

        // I write it down; an insecure link is refused.
        var insecure = await mine.Client.PutAsJsonAsync($"/api/v1/orders/{orderId}/delivery/shipment",
            new { courier = 1, folio = "PQ1234", trackingUrl = "http://paquetenvia.mx/r/PQ1234" });
        Assert.Equal(HttpStatusCode.BadRequest, insecure.StatusCode);
        Assert.Equal(CourierShipment.TrackingUrlMessage, await DetailAsync(insecure));
        var written = await mine.Client.PutAsJsonAsync($"/api/v1/orders/{orderId}/delivery/shipment",
            new { courier = 1, folio = "pq 1234", trackingUrl = "https://paquetenvia.mx/r/PQ1234" });
        Assert.Equal(HttpStatusCode.NoContent, written.StatusCode);

        var read = (await mine.Client.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{orderId}")).GetProperty("delivery");
        Assert.Equal((1, "PQ1234", "https://paquetenvia.mx/r/PQ1234"),
            (read.GetProperty("courier").GetInt32(), read.GetProperty("courierFolio").GetString(), read.GetProperty("trackingUrl").GetString()));
        Assert.Equal(
            new[] { (mine.TenantId, "PQ1234") },
            (await _api.QueryAsync(mine.TenantId, db => db.OrderDeliveries.ToListAsync())).Select(d => (d.TenantId, d.CourierFolio!)));
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.OrderDeliveries.ToListAsync()));
        var log = await _api.QueryAsync(mine.TenantId, db => db.AuditLogs.SingleAsync(a => a.Action == "OrderCourierShipmentRegistered"));
        Assert.DoesNotContain("paquetenvia.mx", log.Metadata);
    }

    [SqlServerFact]
    public async Task PublicMenu_ShouldQuoteAndTakeADelivery_WithTheZonesOfTheBusinessOfTheSlugOnly()
    {
        var mine = await _api.SignUpAsync("domicilio-menu-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("domicilio-menu-b", TwoBusinessesApi.FoodBusiness);
        var tacos = await ProductAsync(mine.Client, "Tacos árabes");
        await ZoneAsync(mine.Client, "Interna Centro", 35m, "72000");
        await ZoneAsync(theirs.Client, "Sur", 20m, "72999");
        await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/cash/open", new { openingAmount = 100m });
        var mySlug = await MenuWithOrdersAsync(mine.Client);
        var theirSlug = await MenuWithOrdersAsync(theirs.Client);
        using var customer = _api.AnonymousClient();

        var menu = await TwoBusinessesApi.ReadAsync(await customer.GetAsync($"/api/v1/public/menu/{mySlug}"));
        Assert.True(menu.GetProperty("deliveryAvailable").GetBoolean());

        var covered = await QuoteAsync(customer, mySlug, "72000");
        var otherBusinessZone = await QuoteAsync(customer, mySlug, "72999");
        var theirQuote = await QuoteAsync(customer, theirSlug, "72000");
        Assert.Equal((true, 35m), (covered.GetProperty("covered").GetBoolean(), covered.GetProperty("fee").GetDecimal()));
        Assert.False(otherBusinessZone.GetProperty("covered").GetBoolean());
        Assert.False(theirQuote.GetProperty("covered").GetBoolean());
        Assert.DoesNotContain("Interna", covered.GetRawText());
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.GetAsync($"/api/v1/public/menu/{mySlug}/delivery-quote?postalCode=7200")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.GetAsync("/api/v1/public/menu/no-existe-xyz/delivery-quote?postalCode=72000")).StatusCode);

        var placed = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync($"/api/v1/public/menu/{mySlug}/orders", new
        {
            customerName = "Ana",
            customerPhone = "5512345678",
            items = new[] { new { productId = tacos, quantity = 2 } },
            delivery = new { postalCode = "72000", addressLine = Address },
            expectedTotal = 65m
        }));
        Assert.Equal((65m, 35m), (placed.GetProperty("total").GetDecimal(), placed.GetProperty("deliveryFee").GetDecimal()));
        var refused = await customer.PostAsJsonAsync($"/api/v1/public/menu/{mySlug}/orders", new
        {
            customerName = "Ana",
            customerPhone = "5512345678",
            items = new[] { new { productId = tacos, quantity = 2 } },
            delivery = new { postalCode = "72999", addressLine = Address }
        });
        Assert.Equal(HomeDeliveryRules.NotCoveredMessage("72999"), await DetailAsync(refused));

        Assert.Equal(
            new[] { (mine.TenantId, "72000", 35m) },
            (await _api.QueryAsync(mine.TenantId, db => db.OrderDeliveries.ToListAsync())).Select(d => (d.TenantId, d.PostalCode, d.Fee)));
        Assert.Equal(0, await _api.QueryAsync(theirs.TenantId, db => db.Orders.CountAsync()));
        Assert.Equal(PlacePublicOrderCommandHandler.TableNotDeliveredMessage, await DetailAsync(await customer.PostAsJsonAsync(
            $"/api/v1/public/menu/{mySlug}/orders", new
            {
                customerName = "Ana",
                customerPhone = "5512345678",
                tableLabel = "Mesa 1",
                items = new[] { new { productId = tacos, quantity = 2 } },
                delivery = new { postalCode = "72000", addressLine = Address }
            })));
    }

    private static object DeliveryOrder(Guid product, string postalCode) => new
    {
        orderType = 0,
        channel = 0,
        createdByBusiness = true,
        openTab = false,
        customerName = "Ana",
        customerPhone = "5512345678",
        items = new[] { new { productId = product, quantity = 2, optionIds = Array.Empty<Guid>() } },
        payments = Array.Empty<object>(),
        delivery = new { postalCode, addressLine = Address, references = "Portón verde" }
    };

    private static async Task ZoneAsync(HttpClient owner, string name, decimal fee, params string[] postalCodes)
        => await TwoBusinessesApi.ReadAsync(await owner.PostAsJsonAsync(Zones, new
        {
            name,
            fee,
            minimumOrder = 0m,
            freeDeliveryFrom = (decimal?)null,
            postalCodes,
            isActive = true,
            revision = 0
        }));

    private static async Task<JsonElement> QuoteAsync(HttpClient customer, string slug, string postalCode)
        => await TwoBusinessesApi.ReadAsync(await customer.GetAsync($"/api/v1/public/menu/{slug}/delivery-quote?postalCode={postalCode}"));

    /// <returns>The slug of the menu, now taking orders.</returns>
    private static async Task<string> MenuWithOrdersAsync(HttpClient owner)
        => (await TwoBusinessesApi.ReadAsync(await owner.PutAsJsonAsync(OnlineMenu, new { mode = 2 }))).GetProperty("slug").GetString()!;

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString();
}
