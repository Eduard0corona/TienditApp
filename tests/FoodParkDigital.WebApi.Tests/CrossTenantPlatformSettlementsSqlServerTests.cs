using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.DeliveryPlatforms;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Infrastructure.Tests.Support;
using FoodParkDigital.WebApi.Errors;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The settlements of the delivery platforms' deposits (Fase 22G) through the API, on SQL Server: each business sees and
/// changes only its own settlements and settles only its own orders, the business header of an owner changes nothing, a
/// settlement goes from open to closed with the figures of its orders and back to pending when voided, and they are read
/// with the reports and changed by owners and admins. A statement is compared with the business's own orders only (Fase 22H).
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantPlatformSettlementsSqlServerTests
{
    private const string Password = "Secreto1";
    private static readonly TimeZoneInfo Mexico = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");

    private readonly TwoBusinessesApi _api;

    public CrossTenantPlatformSettlementsSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Settlements_OfAnotherBusiness_ShouldBeInvisible_AndUntouchable()
    {
        var mine = await _api.SignUpAsync("liquida-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("liquida-b", TwoBusinessesApi.FoodBusiness);
        var myOrder = await RappiOrderAsync(mine, "A1");
        await RappiOrderAsync(theirs, "B1");
        var created = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            "/api/v1/platform-settlements", Settlement(OrderChannel.Rappi, 0m, [myOrder.Id], [new { kind = 4, amount = -myOrder.ExpectedNet }])));
        var id = created.GetProperty("settlement").GetProperty("id").GetGuid();

        // The other owner, even with the header of the business (only the platform admin may use it), finds nothing of mine.
        foreach (var (method, url, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Get, $"/api/v1/platform-settlements/{id}", null),
                     (HttpMethod.Put, $"/api/v1/platform-settlements/{id}", Update(1, 0m, [], [])),
                     (HttpMethod.Post, $"/api/v1/platform-settlements/{id}/close", new { revision = 1 }),
                     (HttpMethod.Post, $"/api/v1/platform-settlements/{id}/void", new { revision = 1, reason = "No es mía" })
                 })
        {
            using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
            request.Headers.Add("X-Tenant-Id", mine.TenantId.ToString());
            Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(request)).StatusCode);
        }
        var theirList = await theirs.Client.GetFromJsonAsync<JsonElement>("/api/v1/platform-settlements");
        Assert.Equal(0, theirList.GetProperty("totalCount").GetInt32());
        var theirPending = await theirs.Client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/platform-settlements/pending-orders?channel=Rappi&from={Today().AddDays(-6):yyyy-MM-dd}&to={Today():yyyy-MM-dd}");
        Assert.Equal(["B1"], theirPending.GetProperty("orders").EnumerateArray().Select(o => o.GetProperty("externalOrderNumber").GetString()));
        var theirReceivable = await theirs.Client.GetFromJsonAsync<JsonElement>("/api/v1/platform-settlements/receivable");
        Assert.Equal(1, theirReceivable.EnumerateArray().Single().GetProperty("pendingOrders").GetInt32());

        // Their settlement with my order finds no such order of theirs.
        var refused = await theirs.Client.PostAsJsonAsync("/api/v1/platform-settlements", Settlement(OrderChannel.Rappi, myOrder.ExpectedNet, [myOrder.Id], []));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(PlatformSettlementErrors.OrderNotOfThePlatform, Problem(await refused.Content.ReadAsStringAsync()).Detail);

        // Mine is as I left it, in my business only.
        var detail = await mine.Client.GetFromJsonAsync<JsonElement>($"/api/v1/platform-settlements/{id}");
        Assert.Equal(1, detail.GetProperty("settlement").GetProperty("revision").GetInt32());
        Assert.Equal(
            new[] { mine.TenantId },
            await _api.QueryAsync(mine.TenantId, db => db.PlatformSettlements.Select(s => s.TenantId).ToListAsync()));
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.PlatformSettlementOrders.ToListAsync()));
    }

    [SqlServerFact]
    public async Task Settlement_ShouldGoFromOpenToClosed_WithItsOrdersFigures_AndBackToPendingWhenVoided()
    {
        var owner = await _api.SignUpAsync("liquida-ciclo", TwoBusinessesApi.FoodBusiness);
        var first = await RappiOrderAsync(owner, "BC953");
        var second = await RappiOrderAsync(owner, "BC954");
        var from = Today().AddDays(-6);

        var pending = await owner.Client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/platform-settlements/pending-orders?channel=3&from={from:yyyy-MM-dd}&to={Today():yyyy-MM-dd}");
        var pendingOrders = pending.GetProperty("orders").EnumerateArray().ToList();
        Assert.Equal(["BC953", "BC954"], pendingOrders.Select(o => o.GetProperty("externalOrderNumber").GetString()));
        Assert.All(pendingOrders, o => Assert.True(o.GetProperty("inPeriod").GetBoolean()));
        Assert.Equal(first.ExpectedNet, pendingOrders[0].GetProperty("expectedNet").GetDecimal());

        // The statement brings both, minus a claim of $5: deposited the rest.
        var deposited = first.ExpectedNet + second.ExpectedNet - 5m;
        var response = await owner.Client.PostAsJsonAsync("/api/v1/platform-settlements",
            Settlement(OrderChannel.Rappi, deposited + 1m, [first.Id, second.Id], [new { kind = 2, description = "Faltó un refresco", amount = -5m }]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await TwoBusinessesApi.ReadAsync(response)).GetProperty("settlement");
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal((1m, 2), (created.GetProperty("difference").GetDecimal(), created.GetProperty("orderCount").GetInt32()));

        // A second open one of Rappi waits; closing with a difference of $1 is refused.
        var secondOpen = await owner.Client.PostAsJsonAsync("/api/v1/platform-settlements", Settlement(OrderChannel.Rappi, 0m, [], []));
        Assert.Equal(HttpStatusCode.BadRequest, secondOpen.StatusCode);
        var unbalanced = await owner.Client.PostAsJsonAsync($"/api/v1/platform-settlements/{id}/close", new { revision = 1 });
        Assert.Equal((HttpStatusCode.BadRequest, PlatformSettlement.DoesNotBalanceMessage),
            (unbalanced.StatusCode, Problem(await unbalanced.Content.ReadAsStringAsync()).Detail));

        // An edit over an older revision is someone else's save in between.
        var updated = await TwoBusinessesApi.ReadAsync(await owner.Client.PutAsJsonAsync($"/api/v1/platform-settlements/{id}",
            Update(1, deposited, [first.Id, second.Id], [new { kind = 2, description = "Faltó un refresco", amount = -5m }])));
        Assert.Equal((2, 0m), (updated.GetProperty("settlement").GetProperty("revision").GetInt32(), updated.GetProperty("settlement").GetProperty("difference").GetDecimal()));
        var stale = await owner.Client.PostAsJsonAsync($"/api/v1/platform-settlements/{id}/close", new { revision = 1 });
        Assert.Equal((HttpStatusCode.Conflict, ApiErrorCodes.ConcurrencyConflict), (stale.StatusCode, Problem(await stale.Content.ReadAsStringAsync()).Code));

        var closed = await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync($"/api/v1/platform-settlements/{id}/close", new { revision = 2 }));
        Assert.Equal((int)PlatformSettlementStatus.Closed, closed.GetProperty("settlement").GetProperty("status").GetInt32());
        Assert.Equal(1, await _api.QueryAsync(owner.TenantId, db => db.AuditLogs.CountAsync(log => log.Action == "PlatformSettlementClosed")));
        var receivable = (await owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/platform-settlements/receivable")).EnumerateArray().Single();
        Assert.Equal((0, Today()), (receivable.GetProperty("pendingOrders").GetInt32(), DateOnly.Parse(receivable.GetProperty("settledThrough").GetString()!)));

        // A closed settlement keeps its orders: another one of Rappi cannot take them, and says why.
        var taken = await owner.Client.PostAsJsonAsync("/api/v1/platform-settlements", Settlement(OrderChannel.Rappi, first.ExpectedNet, [first.Id], []));
        var takenProblem = Problem(await taken.Content.ReadAsStringAsync());
        Assert.Equal((HttpStatusCode.Conflict, ApiErrorCodes.Conflict, PlatformSettlementErrors.OrderTaken),
            (taken.StatusCode, takenProblem.Code, takenProblem.Detail));

        // Voided, it stays as history and its orders are pending again.
        var voided = await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync($"/api/v1/platform-settlements/{id}/void",
            new { revision = 3, reason = "Se capturó con el monto de otra semana" }));
        Assert.Equal((int)PlatformSettlementStatus.Voided, voided.GetProperty("settlement").GetProperty("status").GetInt32());
        var again = await owner.Client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/platform-settlements/pending-orders?channel=Rappi&from={from:yyyy-MM-dd}&to={Today():yyyy-MM-dd}");
        Assert.Equal(2, again.GetProperty("orders").GetArrayLength());
        var list = await owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/platform-settlements?status=Voided");
        Assert.Equal(id, list.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid());
    }

    [SqlServerFact]
    public async Task Settlements_ShouldBeReadWithTheReports_AndChangedByOwnersAndAdmins()
    {
        var owner = await _api.SignUpAsync("liquida-equipo", TwoBusinessesApi.FoodBusiness);
        var order = await RappiOrderAsync(owner, "R1");
        var created = await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync(
            "/api/v1/platform-settlements", Settlement(OrderChannel.Rappi, order.ExpectedNet, [order.Id], [])));
        var id = created.GetProperty("settlement").GetProperty("id").GetGuid();
        var reports = await EmployeeAsync(owner, appRole: 2, "ViewReports");
        var cashier = await EmployeeAsync(owner, appRole: 3, "ManageOrders");
        var reads = new[]
        {
            "/api/v1/platform-settlements",
            "/api/v1/platform-settlements/receivable",
            $"/api/v1/platform-settlements/pending-orders?channel=Rappi&from={Today().AddDays(-6):yyyy-MM-dd}&to={Today():yyyy-MM-dd}",
            $"/api/v1/platform-settlements/{id}"
        };

        foreach (var url in reads)
        {
            Assert.Equal(HttpStatusCode.OK, (await reports.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(url)).StatusCode);
        }
        foreach (var staff in new[] { reports, cashier })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/v1/platform-settlements", Settlement(OrderChannel.UberEats, 0m, [], []))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync($"/api/v1/platform-settlements/{id}", Update(1, 0m, [], []))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync($"/api/v1/platform-settlements/{id}/close", new { revision = 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync($"/api/v1/platform-settlements/{id}/void", new { revision = 1, reason = "x" })).StatusCode);
        }
        Assert.Equal(1, await _api.QueryAsync(owner.TenantId, db => db.PlatformSettlements.Select(s => s.Revision).SingleAsync()));
    }

    [SqlServerFact]
    public async Task Settlements_ShouldBeRefused_WhenNotOfAPlatform_OrNotSaidWhole()
    {
        var owner = await _api.SignUpAsync("liquida-validas", TwoBusinessesApi.FoodBusiness);

        var counter = await owner.Client.PostAsJsonAsync("/api/v1/platform-settlements", Settlement(OrderChannel.Counter, 0m, [], []));
        var withoutDeposit = await owner.Client.PostAsJsonAsync("/api/v1/platform-settlements", new
        {
            channel = 3, periodFrom = Today().AddDays(-6), periodTo = Today(), depositedOn = Today(), orderIds = Array.Empty<Guid>(), adjustments = Array.Empty<object>()
        });
        var nullAdjustment = await owner.Client.PostAsync("/api/v1/platform-settlements", new StringContent(
            $"{{\"channel\":3,\"periodFrom\":\"{Today().AddDays(-6):yyyy-MM-dd}\",\"periodTo\":\"{Today():yyyy-MM-dd}\",\"depositedOn\":\"{Today():yyyy-MM-dd}\",\"depositedAmount\":0,\"orderIds\":[],\"adjustments\":[null]}}",
            System.Text.Encoding.UTF8, "application/json"));
        var thirdDecimal = await owner.Client.PostAsJsonAsync("/api/v1/platform-settlements", Settlement(OrderChannel.Rappi, 10.005m, [], []));
        var withoutPeriod = await owner.Client.GetAsync("/api/v1/platform-settlements/pending-orders?channel=Rappi");

        Assert.Equal((HttpStatusCode.BadRequest, ApiErrorCodes.ValidationFailed), (counter.StatusCode, Problem(await counter.Content.ReadAsStringAsync()).Code));
        Assert.Equal((HttpStatusCode.BadRequest, ApiErrorCodes.ValidationFailed), (withoutDeposit.StatusCode, Problem(await withoutDeposit.Content.ReadAsStringAsync()).Code));
        var nulls = Problem(await nullAdjustment.Content.ReadAsStringAsync());
        Assert.Equal((HttpStatusCode.BadRequest, ApiErrorCodes.ValidationFailed, PlatformSettlementErrors.EmptyAdjustment),
            (nullAdjustment.StatusCode, nulls.Code, nulls.Detail));
        Assert.Equal((HttpStatusCode.BadRequest, PlatformSettlement.DepositedAmountMessage),
            (thirdDecimal.StatusCode, Problem(await thirdDecimal.Content.ReadAsStringAsync()).Detail));
        Assert.Equal(HttpStatusCode.BadRequest, withoutPeriod.StatusCode);
        Assert.Equal(0, await _api.QueryAsync(owner.TenantId, db => db.PlatformSettlements.CountAsync()));
    }

    [SqlServerFact]
    public async Task StatementMatch_ShouldFindOnlyTheBusinessesOwnOrders_AndBeForOwnersAndAdmins()
    {
        var mine = await _api.SignUpAsync("estado-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("estado-b", TwoBusinessesApi.FoodBusiness);
        var myOrder = await RappiOrderAsync(mine, "SM1");
        var theirOrder = await RappiOrderAsync(theirs, "SM1");
        var created = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            "/api/v1/platform-settlements", Settlement(OrderChannel.Rappi, 0m, [], [new { kind = 4, amount = -1m }])));
        var id = created.GetProperty("settlement").GetProperty("id").GetGuid();
        object Statement(Guid? settlementId) => new
        {
            channel = 3,
            settlementId,
            periodFrom = Today().AddDays(-6),
            periodTo = Today(),
            lines = new object[]
            {
                new { row = 2, folio = "sm1", amount = 100m, concept = "Pedido" },
                new { row = 3, folio = (string?)null, amount = -12.5m, concept = "Publicidad" }
            }
        };

        // Mine finds my order; theirs, with the same folio, finds theirs, and cannot compare with my settlement.
        var myMatch = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync("/api/v1/platform-settlements/statement-matches", Statement(id)));
        var folio = myMatch.GetProperty("folios").EnumerateArray().Single();
        Assert.Equal(("SM1", (int)PlatformStatementMatchStatus.Pending, myOrder.Id, 100m - myOrder.ExpectedNet),
            (folio.GetProperty("folio").GetString(), folio.GetProperty("status").GetInt32(),
                folio.GetProperty("orders").EnumerateArray().Single().GetProperty("orderId").GetGuid(), folio.GetProperty("difference").GetDecimal()));
        Assert.Equal(-12.5m, myMatch.GetProperty("conceptsTotal").GetDecimal());
        var theirMatch = await TwoBusinessesApi.ReadAsync(await theirs.Client.PostAsJsonAsync("/api/v1/platform-settlements/statement-matches", Statement(null)));
        Assert.Equal(theirOrder.Id, theirMatch.GetProperty("folios")[0].GetProperty("orders")[0].GetProperty("orderId").GetGuid());
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform-settlements/statement-matches") { Content = JsonContent.Create(Statement(id)) })
        {
            request.Headers.Add("X-Tenant-Id", mine.TenantId.ToString());
            Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(request)).StatusCode);
        }

        // Comparing is part of saving: whoever only reads the reports cannot.
        var reports = await EmployeeAsync(mine, appRole: 2, "ViewReports");
        Assert.Equal(HttpStatusCode.Forbidden, (await reports.PostAsJsonAsync("/api/v1/platform-settlements/statement-matches", Statement(id))).StatusCode);

        var nullLine = await mine.Client.PostAsync("/api/v1/platform-settlements/statement-matches", new StringContent(
            $"{{\"channel\":3,\"periodFrom\":\"{Today().AddDays(-6):yyyy-MM-dd}\",\"periodTo\":\"{Today():yyyy-MM-dd}\",\"lines\":[null]}}",
            System.Text.Encoding.UTF8, "application/json"));
        var nulls = Problem(await nullLine.Content.ReadAsStringAsync());
        Assert.Equal((HttpStatusCode.BadRequest, ApiErrorCodes.ValidationFailed, MatchPlatformStatementQueryValidator.MissingLineMessage),
            (nullLine.StatusCode, nulls.Code, nulls.Detail));
        // Nothing was saved.
        Assert.Empty(await _api.QueryAsync(mine.TenantId, db => db.PlatformSettlementOrders.ToListAsync()));
    }

    /// <summary>The date of the business today (the default time zone of a new business).</summary>
    private static DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Mexico).DateTime);

    private static object Settlement(OrderChannel channel, decimal depositedAmount, Guid[] orderIds, object[] adjustments) => new
    {
        channel = (int)channel,
        periodFrom = Today().AddDays(-6),
        periodTo = Today(),
        depositedOn = Today(),
        depositedAmount,
        reference = "SPEI 4471",
        orderIds,
        adjustments
    };

    private static object Update(int revision, decimal depositedAmount, Guid[] orderIds, object[] adjustments) => new
    {
        revision,
        periodFrom = Today().AddDays(-6),
        periodTo = Today(),
        depositedOn = Today(),
        depositedAmount,
        orderIds,
        adjustments
    };

    /// <summary>An order of Rappi of one product, with Rappi turned on at 30 %, and what it is expected to bring.</summary>
    private async Task<(Guid Id, decimal ExpectedNet)> RappiOrderAsync(SignedUp owner, string folio)
    {
        var terms = await owner.Client.PutAsJsonAsync("/api/v1/delivery-platforms/Rappi", new
        {
            revision = await _api.QueryAsync(owner.TenantId, db => db.DeliveryPlatformSettings.Select(s => s.Revision).SingleOrDefaultAsync()),
            isActive = true,
            commissionPercent = 30m,
            commissionBase = 0,
            commissionVatPercent = 16m,
            isrWithholdingPercent = 2.5m,
            vatWithholdingPercent = 50m
        });
        Assert.True(terms.IsSuccessStatusCode, await terms.Content.ReadAsStringAsync());
        var product = await ProductAsync(owner.Client, $"Paquete {folio}");
        var catalog = await owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/products?isAvailable=true&priceChannel=Rappi&pageSize=500");
        var price = catalog.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == product).GetProperty("priceAmount").GetDecimal();
        var order = await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync("/api/v1/orders", new
        {
            channel = (int)OrderChannel.Rappi,
            externalOrderNumber = folio,
            expectedTotal = price,
            items = new[] { new { productId = product, quantity = 1 } }
        }));
        var id = order.GetProperty("id").GetGuid();
        var expectedNet = await _api.QueryAsync(owner.TenantId, db => db.PlatformOrderDetails.Where(d => d.OrderId == id).Select(d => d.ExpectedSettlement).SingleAsync());
        return (id, expectedNet);
    }

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

    private static (string? Code, string? Detail) Problem(string body)
    {
        var problem = JsonDocument.Parse(body).RootElement;
        return (
            problem.TryGetProperty("code", out var code) ? code.GetString() : null,
            problem.TryGetProperty("detail", out var detail) ? detail.GetString() : null);
    }
}
