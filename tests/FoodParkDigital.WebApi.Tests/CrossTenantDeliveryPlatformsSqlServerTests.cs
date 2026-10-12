using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.DeliveryPlatforms;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using FoodParkDigital.WebApi.Errors;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantHttpSqlServerTests;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The delivery platforms of a business (Fase 22F) through the API: each owner reads and changes only the terms of their
/// own business, the business header of an owner changes nothing, a save over an older revision answers 409, the rates
/// are for owners and admins (whoever takes orders or reads the reports learns only which platforms are on), a business
/// registers orders only of the platforms it turned on, and the orders of a platform of one business never show in the
/// lists, searches or reports of another.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantDeliveryPlatformsSqlServerTests
{
    private const string Password = "Secreto1";

    private readonly TwoBusinessesApi _api;

    public CrossTenantDeliveryPlatformsSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Platforms_OfAnotherBusiness_ShouldBeInvisible_AndUntouchable()
    {
        var mine = await _api.SignUpAsync("plataformas-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("plataformas-b", TwoBusinessesApi.FoodBusiness);
        var saved = await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi", Terms(revision: 0, commission: 30m)));
        Assert.Equal((true, 1), (Rappi(saved).GetProperty("isActive").GetBoolean(), Rappi(saved).GetProperty("revision").GetInt32()));

        // The other owner, even with the header of the business (only the platform admin may use it), sees Rappi as
        // never saved and no platform on.
        using var read = new HttpRequestMessage(HttpMethod.Get, "/api/v1/delivery-platforms");
        read.Headers.Add("X-Tenant-Id", mine.TenantId.ToString());
        var theirView = await TwoBusinessesApi.ReadAsync(await theirs.Client.SendAsync(read));
        Assert.False(Rappi(theirView).GetProperty("isConfigured").GetBoolean());
        Assert.Equal(JsonValueKind.Null, Rappi(theirView).GetProperty("commissionPercent").ValueKind);
        Assert.Equal(0, Rappi(theirView).GetProperty("revision").GetInt32());
        Assert.Equal(0, (await theirs.Client.GetFromJsonAsync<JsonElement>("/api/v1/delivery-platforms/channels")).GetArrayLength());

        // Their saves, with the header too, make and change their own Rappi.
        using var save = new HttpRequestMessage(HttpMethod.Put, "/api/v1/delivery-platforms/3")
        {
            Content = JsonContent.Create(Terms(revision: 0, commission: 18m, isActive: false))
        };
        save.Headers.Add("X-Tenant-Id", mine.TenantId.ToString());
        var theirSave = await TwoBusinessesApi.ReadAsync(await theirs.Client.SendAsync(save));
        Assert.Equal((18m, 1), (Rappi(theirSave).GetProperty("commissionPercent").GetDecimal(), Rappi(theirSave).GetProperty("revision").GetInt32()));
        Assert.Equal(HttpStatusCode.OK, (await theirs.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi", Terms(revision: 1, commission: 19m))).StatusCode);

        // Mine is as it was, with who changed it.
        var myRappi = Rappi(await mine.Client.GetFromJsonAsync<JsonElement>("/api/v1/delivery-platforms"));
        Assert.Equal((30m, true, 1), (myRappi.GetProperty("commissionPercent").GetDecimal(), myRappi.GetProperty("isActive").GetBoolean(), myRappi.GetProperty("revision").GetInt32()));
        Assert.Equal(mine.Email, myRappi.GetProperty("changedBy").GetString());
        Assert.Equal(561.48m, myRappi.GetProperty("example").GetProperty("expectedSettlement").GetDecimal());
        var myAudit = await _api.QueryAsync(mine.TenantId, db => db.AuditLogs.Where(a => a.Action == "DeliveryPlatformUpdated").ToListAsync());
        Assert.Equal(mine.TenantId, Assert.Single(myAudit).TenantId);
        Assert.Equal(2, await _api.QueryAsync(theirs.TenantId, db => db.AuditLogs.CountAsync(a => a.Action == "DeliveryPlatformUpdated")));
    }

    [SqlServerFact]
    public async Task SaveOverAnOlderRevision_ShouldAnswer409ConcurrencyConflict()
    {
        var owner = await _api.SignUpAsync("plataformas-409", TwoBusinessesApi.FoodBusiness);
        await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/UberEats", Terms(revision: 0, commission: 25m)));
        await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/UberEats", Terms(revision: 1, commission: 27m)));

        var stale = await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/UberEats", Terms(revision: 1, commission: 29m));
        var notAPlatform = await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Counter", Terms(revision: 0, commission: 29m));
        var outOfRange = await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/UberEats", Terms(revision: 2, commission: 61m));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var problem = JsonDocument.Parse(await stale.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(ApiErrorCodes.ConcurrencyConflict, problem.GetProperty("code").GetString());
        Assert.Equal(DeliveryPlatformErrors.Stale, problem.GetProperty("detail").GetString());
        await AssertValidationAsync(notAPlatform, "Esa no es una plataforma de reparto.");
        await AssertValidationAsync(outOfRange, "La comisión va de 0 % a 60 %, con hasta dos decimales.");
        var uber = (await owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/delivery-platforms")).GetProperty("platforms")[1];
        Assert.Equal((27m, 2), (uber.GetProperty("commissionPercent").GetDecimal(), uber.GetProperty("revision").GetInt32()));
    }

    [SqlServerFact]
    public async Task SaveWithoutATerm_ShouldBeRefused_InsteadOfSavingItAsZero()
    {
        var owner = await _api.SignUpAsync("plataformas-sin-comision", TwoBusinessesApi.FoodBusiness);

        // No commission typed: nothing is saved with 0 % (a net from a contract nobody wrote would be false).
        var first = await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi", new
        {
            revision = 0,
            isActive = true,
            commissionBase = 0,
            commissionVatPercent = 16m,
            isrWithholdingPercent = 2.5m,
            vatWithholdingPercent = 50m
        });
        var bare = await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi", new { revision = 0, isActive = true });

        foreach (var refused in new[] { first, bare })
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal(ApiErrorCodes.ValidationFailed, JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
        }
        var rappi = Rappi(await owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/delivery-platforms"));
        Assert.Equal((false, 0), (rappi.GetProperty("isConfigured").GetBoolean(), rappi.GetProperty("revision").GetInt32()));

        // A saved platform keeps its terms when a change leaves one out.
        await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi", Terms(revision: 0, commission: 30m)));
        var partial = await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi", new { revision = 1, isActive = true, commissionPercent = 25m });
        Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);
        rappi = Rappi(await owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/delivery-platforms"));
        Assert.Equal((30m, 16m, 1), (
            rappi.GetProperty("commissionPercent").GetDecimal(),
            rappi.GetProperty("commissionVatPercent").GetDecimal(),
            rappi.GetProperty("revision").GetInt32()));
    }

    [SqlServerFact]
    public async Task Staff_ShouldNotReadOrChangeTheTerms_AndWhoSellsOrReadsReportsSeeOnlyWhichAreOn()
    {
        var owner = await _api.SignUpAsync("plataformas-equipo", TwoBusinessesApi.FoodBusiness);
        await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi", Terms(revision: 0, commission: 30m)));
        var kitchen = await EmployeeAsync(owner, appRole: 3, "ManageOrders", "ManageServiceBoard");
        var reports = await EmployeeAsync(owner, appRole: 2, "ViewReports");
        var catalog = await EmployeeAsync(owner, appRole: 2, "ManageCatalog");

        foreach (var staff in new[] { kitchen, reports, catalog })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/delivery-platforms")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi", Terms(revision: 1, commission: 10m))).StatusCode);
        }

        // The kitchen takes the orders of the tablet and the reports filter by platform: which ones are on, never the rates.
        var kitchenChannels = await kitchen.GetAsync("/api/v1/delivery-platforms/channels");
        Assert.Equal(HttpStatusCode.OK, kitchenChannels.StatusCode);
        var body = await kitchenChannels.Content.ReadAsStringAsync();
        var channel = Assert.Single(JsonDocument.Parse(body).RootElement.EnumerateArray());
        Assert.Equal(((int)OrderChannel.Rappi, true), (channel.GetProperty("channel").GetInt32(), channel.GetProperty("isActive").GetBoolean()));
        Assert.DoesNotContain("commission", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await reports.GetAsync("/api/v1/delivery-platforms/channels")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await catalog.GetAsync("/api/v1/delivery-platforms/channels")).StatusCode);
        // The commission of each order is only in the reports.
        Assert.Equal(HttpStatusCode.OK, (await reports.GetAsync("/api/v1/reports/sales-by-channel")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reports.GetAsync("/api/v1/reports/delivery-platform-orders?channel=3")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kitchen.GetAsync("/api/v1/reports/sales-by-channel")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kitchen.GetAsync("/api/v1/reports/delivery-platform-orders?channel=3")).StatusCode);

        var rappi = Rappi(await owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/delivery-platforms"));
        Assert.Equal((30m, 1), (rappi.GetProperty("commissionPercent").GetDecimal(), rappi.GetProperty("revision").GetInt32()));
    }

    [SqlServerFact]
    public async Task OrderOfAPlatform_OnlyAnotherBusinessTurnedOn_ShouldBeRefused()
    {
        var mine = await _api.SignUpAsync("rappi-encendido", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("rappi-apagado", TwoBusinessesApi.FoodBusiness);
        await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi", Terms(revision: 0, commission: 30m)));
        var product = await ProductAsync(theirs.Client, "Paquete de B");

        var response = await theirs.Client.PostAsJsonAsync("/api/v1/orders", RappiOrder(product, "BC953"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(ApiErrorCodes.BusinessRule, problem.GetProperty("code").GetString());
        Assert.Equal(PlatformOrderErrors.NotActive(OrderChannel.Rappi), problem.GetProperty("detail").GetString());
        Assert.Equal(0, await _api.QueryAsync(theirs.TenantId, db => db.Orders.CountAsync()));
    }

    [SqlServerFact]
    public async Task OrderOfAPlatform_OfAnotherBusiness_ShouldNotShowInItsListsSearchesOrReports()
    {
        var mine = await _api.SignUpAsync("rappi-propio", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("rappi-ajeno", TwoBusinessesApi.FoodBusiness);
        await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi", Terms(revision: 0, commission: 30m)));
        await TwoBusinessesApi.ReadAsync(await theirs.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi", Terms(revision: 0, commission: 30m)));
        var product = await ProductAsync(mine.Client, "Paquete de A");
        var order = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync("/api/v1/orders", RappiOrder(product, "XA953")));
        var orderId = order.GetProperty("id").GetGuid().ToString();
        var total = order.GetProperty("total").GetDecimal();

        // Its own lists show it...
        Assert.Contains(orderId, await mine.Client.GetStringAsync("/api/v1/orders?search=XA953&pageSize=10"));
        var myChannels = await mine.Client.GetFromJsonAsync<JsonElement>("/api/v1/reports/sales-by-channel");
        Assert.Equal(total, Row(myChannels, OrderChannel.Rappi).GetProperty("platformCollected").GetDecimal());
        var myOrders = await mine.Client.GetStringAsync("/api/v1/reports/delivery-platform-orders?channel=Rappi");
        Assert.Contains(orderId, myOrders);
        Assert.Contains("XA953", myOrders);

        // ...and the other business's never do, though it has Rappi on too.
        foreach (var path in new[]
        {
            "/api/v1/orders?pageSize=100",
            "/api/v1/orders?search=XA953&pageSize=10",
            "/api/v1/reports/sales-by-channel",
            "/api/v1/reports/delivery-platform-orders?channel=3",
            $"/api/v1/kitchen/orders"
        })
        {
            var body = await BodyAsync(theirs.Client, path);
            Assert.DoesNotContain(orderId, body);
            Assert.DoesNotContain("XA953", body);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync($"/api/v1/orders/{orderId}")).StatusCode);
        var theirChannels = await theirs.Client.GetFromJsonAsync<JsonElement>("/api/v1/reports/sales-by-channel");
        Assert.Equal(0m, theirChannels.GetProperty("totals").GetProperty("paymentsAmount").GetDecimal());
        Assert.Equal(0m, Row(theirChannels, OrderChannel.Rappi).GetProperty("platformCollected").GetDecimal());
        var theirOrders = await theirs.Client.GetFromJsonAsync<JsonElement>("/api/v1/reports/delivery-platform-orders?channel=3");
        Assert.Equal(0, theirOrders.GetProperty("totalCount").GetInt32());
    }

    private static object Terms(int revision, decimal commission, bool isActive = true) => new
    {
        revision,
        isActive,
        commissionPercent = commission,
        commissionBase = 0,
        commissionVatPercent = 16m,
        isrWithholdingPercent = 2.5m,
        vatWithholdingPercent = 50m
    };

    private static object RappiOrder(Guid product, string folio) => new
    {
        channel = (int)OrderChannel.Rappi,
        externalOrderNumber = folio,
        customerName = "Juan",
        items = new[] { Line(product) }
    };

    private static JsonElement Rappi(JsonElement platforms)
        => platforms.GetProperty("platforms").EnumerateArray().Single(p => p.GetProperty("channel").GetInt32() == (int)OrderChannel.Rappi);

    private static JsonElement Row(JsonElement report, OrderChannel channel)
        => report.GetProperty("channels").EnumerateArray().Single(c => c.GetProperty("channel").ValueKind == JsonValueKind.Number
            && c.GetProperty("channel").GetInt32() == (int)channel);

    /// <summary>A member of the owner's team with the role and the permissions given, signed in with its own password.</summary>
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

    private static async Task AssertValidationAsync(HttpResponseMessage response, string message)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(ApiErrorCodes.ValidationFailed, problem.GetProperty("code").GetString());
        Assert.Contains(message, problem.GetRawText());
    }

    /// <summary>The body of a read that may also be refused, as long as it is not a failure of the app.</summary>
    private static async Task<string> BodyAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.True((int)response.StatusCode < 500, $"{path} answered {(int)response.StatusCode}");
        return await response.Content.ReadAsStringAsync();
    }
}
