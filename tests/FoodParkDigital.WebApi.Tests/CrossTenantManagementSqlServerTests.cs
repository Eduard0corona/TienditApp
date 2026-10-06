using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Infrastructure.Tests.Support;
using static FoodParkDigital.WebApi.Tests.CrossTenantHttpSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// What an owner manages besides sales and stock: tables, branches, the team, campaigns and reports. The owner of one
/// business tries to read, change and use the other's by their ids: every attempt fails as if they did not exist,
/// nothing of the other business changes, and its customers and numbers never show in the other's lists, audiences
/// or reports. Runs where TIENDITAPP_TEST_SQLSERVER names a server, like <see cref="CrossTenantHttpSqlServerTests"/>.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantManagementSqlServerTests
{
    // The phones of the campaigns' third business: a registered customer, a buyer and one that opted out.
    private const string CustomerOfC = "5533334444";
    private const string BuyerOfC = "5544445555";
    private const string OptedOutOfC = "5566667777";

    private readonly TwoBusinessesApi _api;

    public CrossTenantManagementSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    private Business A => _api.A;
    private Business B => _api.B;

    [SqlServerFact]
    public async Task Tables_OfAnotherBusiness_ShouldBeInvisible_AndTakeNoTabs()
    {
        // Tables are for food businesses: two of them.
        var mine = await _api.SignUpAsync("mesas-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("mesas-b", TwoBusinessesApi.FoodBusiness);
        var product = await ProductAsync(mine.Client, "Taco de A");
        var theirProduct = await ProductAsync(theirs.Client, "Taco de B");
        var table = await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/tables", new { name = "Mesa 1 de A", seats = 4, sortOrder = 1 });
        var tab = await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/orders", Tab(product, table, tabName: null));
        var theirTab = await TwoBusinessesApi.IdAsync(theirs.Client, "/api/v1/orders", Tab(theirProduct, table: null, tabName: "Barra"));
        var before = await TableAsync(mine.Client, table);

        AssertRefused(await theirs.Client.PutAsJsonAsync($"/api/v1/tables/{table}", new { name = "Cambiada por B", seats = 1, sortOrder = 9, isActive = false }));
        // A tab of theirs at the table: opened there or moved to it.
        AssertRefused(await theirs.Client.PostAsJsonAsync("/api/v1/orders", Tab(theirProduct, table, tabName: null)));
        AssertRefused(await theirs.Client.PutAsJsonAsync($"/api/v1/orders/{theirTab}/table", new { diningTableId = table }));
        // The tab at the table: no round added, not closed.
        AssertRefused(await theirs.Client.PostAsJsonAsync($"/api/v1/orders/{tab}/rounds", new { items = new[] { Line(theirProduct) } }));
        AssertRefused(await theirs.Client.PostAsync($"/api/v1/orders/{tab}/close-tab", null));
        var theirRoom = await theirs.Client.GetStringAsync("/api/v1/tables?includeInactive=true");
        Assert.DoesNotContain(table.ToString(), theirRoom);
        Assert.DoesNotContain(tab.ToString(), theirRoom);

        var stillThere = await TableAsync(mine.Client, table);
        Assert.Equal("Mesa 1 de A", stillThere.GetProperty("name").GetString());
        Assert.True(stillThere.GetProperty("isActive").GetBoolean());
        Assert.Equal(tab, stillThere.GetProperty("openTab").GetProperty("orderId").GetGuid());
        Assert.Equal(
            before.GetProperty("openTab").GetProperty("rounds").GetInt32(),
            stillThere.GetProperty("openTab").GetProperty("rounds").GetInt32());
    }

    [SqlServerFact]
    public async Task Branches_OfAnotherBusiness_ShouldBeInvisible_AndTakeNothingOfTheirs()
    {
        var before = await DefaultBranchAsync(A.Client);
        var branch = before.GetProperty("id").GetGuid();
        var path = $"/api/v1/branches/{branch}";

        AssertRefused(await B.Client.PutAsJsonAsync(path, new { id = branch, name = "Cambiada por B", code = "B9" }));
        AssertRefused(await B.Client.PatchAsJsonAsync($"{path}/active", new { isActive = false }));
        AssertRefused(await B.Client.PostAsync($"{path}/default", null));
        // A sale or a warehouse of theirs in it.
        AssertRefused(await B.Client.PostAsJsonAsync("/api/v1/orders", new
        {
            orderType = 0,
            channel = 0,
            createdByBusiness = true,
            customerName = "Mostrador",
            branchId = branch,
            items = new[] { Line(B.ProductId) },
            payments = Array.Empty<object>()
        }));
        AssertRefused(await B.Client.PostAsJsonAsync("/api/v1/inventory/locations", new { name = "Bodega de B", code = "BB1", branchId = branch }));
        foreach (var list in new[]
        {
            "/api/v1/branches",
            $"/api/v1/cash/sessions?branchId={branch}&pageSize=100",
            $"/api/v1/reports/sales-by-branch?branchId={branch}"
        })
        {
            var theirs = await BodyAsync(B.Client, list);
            Assert.DoesNotContain(branch.ToString(), theirs);
            Assert.DoesNotContain(A.CashSessionId.ToString(), theirs);
        }

        var stillThere = await DefaultBranchAsync(A.Client);
        Assert.Equal(branch, stillThere.GetProperty("id").GetGuid());
        Assert.Equal(before.GetProperty("name").GetString(), stillThere.GetProperty("name").GetString());
        Assert.Equal(before.GetProperty("code").GetString(), stillThere.GetProperty("code").GetString());
        Assert.True(stillThere.GetProperty("isActive").GetBoolean());
    }

    [SqlServerFact]
    public async Task Team_OfAnotherBusiness_ShouldBeInvisibleAndUntouchable()
    {
        var email = $"empleado-{Guid.NewGuid():N}@demo.com";
        var invited = await TwoBusinessesApi.ReadAsync(await A.Client.PostAsJsonAsync("/api/v1/employees", new
        {
            email,
            firstName = "Empleado",
            lastName = "De A",
            appRole = 2,
            tenantRole = 2
        }));
        var employee = invited.GetProperty("tenantUserId").GetGuid();
        var path = $"/api/v1/employees/{employee}";
        var before = await A.Client.GetFromJsonAsync<JsonElement>(path);

        Assert.Equal(HttpStatusCode.NotFound, (await B.Client.GetAsync(path)).StatusCode);
        AssertRefused(await B.Client.PutAsJsonAsync(path, new
        {
            tenantUserId = employee,
            firstName = "Cambiado",
            lastName = "Por B",
            appRole = 1,
            tenantRole = 1
        }));
        AssertRefused(await B.Client.PatchAsJsonAsync($"{path}/active", new { isActive = false }));
        // A new invitation link would let them choose the person's password.
        AssertRefused(await B.Client.PostAsync($"{path}/resend-invitation", null));
        var theirTeam = await B.Client.GetStringAsync("/api/v1/employees?pageSize=100");
        Assert.DoesNotContain(employee.ToString(), theirTeam);
        Assert.DoesNotContain(email, theirTeam);

        var stillThere = await A.Client.GetFromJsonAsync<JsonElement>(path);
        foreach (var field in new[] { "firstName", "lastName", "appRole", "tenantRole", "status", "isActive", "permissions" })
            Assert.Equal(before.GetProperty(field).GetRawText(), stillThere.GetProperty(field).GetRawText());
    }

    [SqlServerFact]
    public async Task Campaigns_OfAnotherBusiness_ShouldBeInvisible_AndReachNoneOfItsCustomers()
    {
        // A business with a registered customer, a buyer, its own opt-out, WhatsApp connected, a campaign template and
        // a campaign for tomorrow.
        var c = await _api.SignUpAsync("campanas");
        await TwoBusinessesApi.IdAsync(c.Client, "/api/v1/customers", new { name = "Clienta de C", phone = CustomerOfC });
        await PaidSaleAsync(c.Client, await ProductAsync(c.Client, "Corte de C"), BuyerOfC, "Comprador de C");
        await OptOutAsync(c.Client, OptedOutOfC);
        await TwoBusinessesApi.ReadAsync(await c.Client.PutAsJsonAsync("/api/v1/auth/my-tenant/whatsapp", new
        {
            isEnabled = true,
            accessToken = "token-de-prueba",
            phoneNumberId = $"2{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}",
            wabaId = "waba-de-c",
            displayPhoneNumber = "+52 55 0000 0003"
        }));
        var template = await TwoBusinessesApi.IdAsync(c.Client, "/api/v1/messaging/templates", new
        {
            name = "Promo de C",
            purpose = 0,
            body = "Hola, hoy hay descuento."
        });
        var campaign = await TwoBusinessesApi.IdAsync(c.Client, "/api/v1/messaging/campaigns", new
        {
            name = "Campaña de C",
            templateId = template,
            audienceKind = 0,
            minimumOrders = 1,
            scheduledAt = DateTimeOffset.UtcNow.AddDays(1)
        });
        var campaignBefore = await c.Client.GetFromJsonAsync<JsonElement>($"/api/v1/messaging/campaigns/{campaign}");

        Assert.Equal(HttpStatusCode.NotFound, (await B.Client.GetAsync($"/api/v1/messaging/campaigns/{campaign}")).StatusCode);
        AssertRefused(await B.Client.PostAsync($"/api/v1/messaging/campaigns/{campaign}/cancel", null));
        AssertRefused(await B.Client.PutAsJsonAsync($"/api/v1/messaging/templates/{template}", new
        {
            name = "Cambiada por B",
            body = "Otro texto",
            isActive = false
        }));
        AssertRefused(await B.Client.PostAsJsonAsync("/api/v1/messaging/campaigns/preview", new { templateId = template, audienceKind = 0, minimumOrders = 1 }));
        AssertRefused(await B.Client.PostAsJsonAsync("/api/v1/messaging/campaigns", new
        {
            name = "Campaña de B",
            templateId = template,
            audienceKind = 0,
            minimumOrders = 1
        }));
        foreach (var list in new[]
        {
            "/api/v1/messaging/templates",
            "/api/v1/messaging/campaigns?pageSize=100",
            "/api/v1/messaging/opt-outs?pageSize=100"
        })
        {
            var theirs = await B.Client.GetStringAsync(list);
            Assert.DoesNotContain(template.ToString(), theirs);
            Assert.DoesNotContain(campaign.ToString(), theirs);
            Assert.DoesNotContain(OptedOutOfC, theirs);
        }

        // B's own audiences: everyone with a phone, and the buyers. None of C's customers.
        var theirTemplate = await TwoBusinessesApi.IdAsync(B.Client, "/api/v1/messaging/templates", new
        {
            name = $"Promo de B {Guid.NewGuid():N}"[..20],
            purpose = 0,
            body = "Hola, hoy hay descuento."
        });
        foreach (var audienceKind in new[] { 0, 1 })
        {
            var preview = await PreviewAsync(B.Client, theirTemplate, audienceKind);
            Assert.DoesNotContain(preview.GetProperty("samples").EnumerateArray(), sample => IsCustomerOfC(sample));
        }

        // B's opt-out of a phone C's customer also has: C keeps reaching her.
        await OptOutAsync(B.Client, CustomerOfC);
        var mine = await PreviewAsync(c.Client, template, audienceKind: 0);
        Assert.Equal(2, mine.GetProperty("recipientCount").GetInt32());
        Assert.Equal(0, mine.GetProperty("excludedCount").GetInt32());
        Assert.Equal(2, mine.GetProperty("samples").EnumerateArray().Count(sample => IsCustomerOfC(sample)));

        var campaignAfter = await c.Client.GetFromJsonAsync<JsonElement>($"/api/v1/messaging/campaigns/{campaign}");
        Assert.Equal(campaignBefore.GetProperty("status").GetRawText(), campaignAfter.GetProperty("status").GetRawText());
        var templates = await c.Client.GetFromJsonAsync<JsonElement>("/api/v1/messaging/templates");
        var templateAfter = templates.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == template);
        Assert.Equal("Promo de C", templateAfter.GetProperty("name").GetString());
        Assert.True(templateAfter.GetProperty("isActive").GetBoolean());
    }

    [SqlServerFact]
    public async Task Reports_ShouldCountOnlyTheirOwnBusiness()
    {
        var summaryBefore = await B.Client.GetFromJsonAsync<JsonElement>("/api/v1/reports/operational-summary");
        var salesBefore = await B.Client.GetFromJsonAsync<JsonElement>("/api/v1/reports/sales-by-branch");

        // Another business sells and gets paid.
        var c = await _api.SignUpAsync("reportes");
        var total = await PaidSaleAsync(c.Client, await ProductAsync(c.Client, "Corte de reportes"), phone: null, name: "Mostrador");
        var branch = (await DefaultBranchAsync(c.Client)).GetProperty("id").GetGuid();

        // Its reports show the sale...
        var summary = await c.Client.GetFromJsonAsync<JsonElement>("/api/v1/reports/operational-summary");
        Assert.Equal(total, summary.GetProperty("totalOrderPayments").GetDecimal());
        var sales = await c.Client.GetFromJsonAsync<JsonElement>("/api/v1/reports/sales-by-branch");
        Assert.Equal(total, sales.GetProperty("totals").GetProperty("paymentsAmount").GetDecimal());

        // ...and B's did not move, its branch included.
        var summaryAfter = await B.Client.GetFromJsonAsync<JsonElement>("/api/v1/reports/operational-summary");
        foreach (var figure in new[] { "totalRevenue", "totalTax", "netRevenue", "totalOrderPayments", "ordersCompleted", "revenueByPaymentMethod" })
            Assert.Equal(summaryBefore.GetProperty(figure).GetRawText(), summaryAfter.GetProperty(figure).GetRawText());
        var salesAfter = await B.Client.GetFromJsonAsync<JsonElement>("/api/v1/reports/sales-by-branch");
        Assert.Equal(salesBefore.GetProperty("totals").GetRawText(), salesAfter.GetProperty("totals").GetRawText());
        var theirBranch = await BodyAsync(B.Client, $"/api/v1/reports/sales-by-branch?branchId={branch}");
        Assert.DoesNotContain(branch.ToString(), theirBranch);
        Assert.DoesNotContain(total.ToString(System.Globalization.CultureInfo.InvariantCulture), theirBranch);
    }

    private static bool IsCustomerOfC(JsonElement sample)
        => sample.GetProperty("recipientName").GetString() is "Clienta de C" or "Comprador de C"
            || sample.GetProperty("phone").GetString() is { } phone
                && (phone.EndsWith(CustomerOfC[^4..], StringComparison.Ordinal) || phone.EndsWith(BuyerOfC[^4..], StringComparison.Ordinal));

    private static async Task<Guid> ProductAsync(HttpClient client, string name)
    {
        var category = await TwoBusinessesApi.IdAsync(client, "/api/v1/categories", new { name = $"Categoría de {name}", sortOrder = 1 });
        return await TwoBusinessesApi.IdAsync(client, "/api/v1/products", NewProduct(category, name));
    }

    /// <returns>The total of the sale, paid in full by card (so it closes as delivered).</returns>
    private static async Task<decimal> PaidSaleAsync(HttpClient client, Guid product, string? phone, string name)
    {
        var order = await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync("/api/v1/orders", new
        {
            orderType = 0,
            channel = 0,
            createdByBusiness = true,
            customerName = name,
            customerPhone = phone,
            items = new[] { Line(product) },
            payments = Array.Empty<object>()
        }));
        var total = order.GetProperty("total").GetDecimal();
        var paid = await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync(
            $"/api/v1/orders/{order.GetProperty("id").GetGuid()}/payments", new { amount = total, method = 1 }));
        Assert.Equal(0m, paid.GetProperty("balance").GetDecimal());
        return total;
    }

    private static object Tab(Guid product, Guid? table, string? tabName) => new
    {
        orderType = 0,
        channel = 0,
        createdByBusiness = true,
        openTab = true,
        diningTableId = table,
        tableNumber = tabName,
        customerName = "Mesa",
        items = new[] { Line(product) },
        payments = Array.Empty<object>()
    };

    private static object Line(Guid product) => new { productId = product, quantity = 1, optionIds = Array.Empty<Guid>() };

    private static async Task<JsonElement> TableAsync(HttpClient client, Guid table)
    {
        var room = await client.GetFromJsonAsync<JsonElement>("/api/v1/tables?includeInactive=true");
        return room.GetProperty("tables").EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == table);
    }

    private static async Task<JsonElement> DefaultBranchAsync(HttpClient client)
    {
        var branches = await client.GetFromJsonAsync<JsonElement>("/api/v1/branches");
        return branches.EnumerateArray().Single(b => b.GetProperty("isDefault").GetBoolean());
    }

    private static async Task OptOutAsync(HttpClient client, string phone)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/messaging/opt-outs", new { phone, optedOut = true });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<JsonElement> PreviewAsync(HttpClient client, Guid template, int audienceKind)
        => await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync("/api/v1/messaging/campaigns/preview", new
        {
            templateId = template,
            audienceKind,
            minimumOrders = 1
        }));

    /// <summary>The body of a read that may also be refused, as long as it is not a failure of the app.</summary>
    private static async Task<string> BodyAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.True((int)response.StatusCode < 500, $"{path} answered {(int)response.StatusCode}");
        return await response.Content.ReadAsStringAsync();
    }
}
