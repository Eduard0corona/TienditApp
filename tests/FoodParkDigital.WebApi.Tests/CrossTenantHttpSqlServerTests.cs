using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.Audit;
using FoodParkDigital.Infrastructure.Tests.Support;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Two businesses on the real API (the whole pipeline in process, SQL Server): the owner of one tries to read, change
/// and delete the other's records by their ids, with the other's header, route and files. Every attempt fails as if
/// the record did not exist, and nothing of the other business changes. Runs where TIENDITAPP_TEST_SQLSERVER names a
/// server (the Migrations job of CI). Hubs and the WhatsApp webhook: <see cref="CrossTenantRealtimeSqlServerTests"/>.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantHttpSqlServerTests
{
    private readonly TwoBusinessesApi _api;

    public CrossTenantHttpSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    private Business A => _api.A;
    private Business B => _api.B;

    [SqlServerFact]
    public async Task Catalog_OfAnotherBusiness_ShouldBeInvisibleAndUntouchable()
    {
        var product = $"/api/v1/products/{A.ProductId}";
        var category = $"/api/v1/categories/{A.CategoryId}";

        Assert.Equal(HttpStatusCode.OK, (await A.Client.GetAsync(product)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await B.Client.GetAsync(product)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await B.Client.GetAsync(category)).StatusCode);
        AssertRefused(await B.Client.PutAsJsonAsync(product, NewProduct(A.CategoryId, "Cambiado por B") with { Id = A.ProductId }));
        AssertRefused(await B.Client.PutAsJsonAsync(category, new { id = A.CategoryId, name = "Cambiada por B", sortOrder = 9 }));
        AssertRefused(await B.Client.DeleteAsync(product));
        AssertRefused(await B.Client.DeleteAsync(category));
        Assert.DoesNotContain(A.ProductId.ToString(), await B.Client.GetStringAsync("/api/v1/products?pageSize=100"));
        Assert.DoesNotContain(A.CategoryId.ToString(), await B.Client.GetStringAsync("/api/v1/categories"));

        var stillThere = await A.Client.GetFromJsonAsync<JsonElement>(product);
        Assert.Equal("Agua de A", stillThere.GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.OK, (await A.Client.GetAsync(category)).StatusCode);
    }

    [SqlServerFact]
    public async Task CustomersAndSuppliers_OfAnotherBusiness_ShouldBeInvisibleAndUntouchable()
    {
        var customer = $"/api/v1/customers/{A.CustomerId}";
        var supplier = $"/api/v1/suppliers/{A.SupplierId}";

        Assert.Equal(HttpStatusCode.OK, (await A.Client.GetAsync(customer)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await B.Client.GetAsync(customer)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await B.Client.GetAsync(supplier)).StatusCode);
        AssertRefused(await B.Client.PutAsJsonAsync(customer, new { id = A.CustomerId, name = "Cambiado por B" }));
        AssertRefused(await B.Client.PutAsJsonAsync(supplier, new { id = A.SupplierId, name = "Cambiado por B" }));
        AssertRefused(await B.Client.DeleteAsync(customer));
        Assert.DoesNotContain(A.CustomerId.ToString(), await B.Client.GetStringAsync("/api/v1/customers?pageSize=100"));
        Assert.DoesNotContain(A.SupplierId.ToString(), await B.Client.GetStringAsync("/api/v1/suppliers"));

        var stillThere = await A.Client.GetFromJsonAsync<JsonElement>(customer);
        Assert.Equal("Cliente de A", stillThere.GetProperty("name").GetString());
        Assert.Equal(
            "Proveedor de A",
            (await A.Client.GetFromJsonAsync<JsonElement>(supplier)).GetProperty("name").GetString());
    }

    [SqlServerFact]
    public async Task Orders_OfAnotherBusiness_ShouldBeInvisible_AndNotMoveOrTakePayments()
    {
        var order = $"/api/v1/orders/{A.OrderId}";
        var before = await A.Client.GetFromJsonAsync<JsonElement>(order);

        Assert.Equal(HttpStatusCode.NotFound, (await B.Client.GetAsync(order)).StatusCode);
        AssertRefused(await B.Client.PutAsJsonAsync($"{order}/status", new { newStatus = 1 }));
        AssertRefused(await B.Client.PostAsJsonAsync($"{order}/payments", new { amount = 5m, method = 0 }));
        var theirOrders = await B.Client.GetStringAsync("/api/v1/orders?pageSize=100");
        Assert.DoesNotContain(A.OrderId.ToString(), theirOrders);
        Assert.DoesNotContain("Agua de A", theirOrders);

        var stillThere = await A.Client.GetFromJsonAsync<JsonElement>(order);
        Assert.Equal(before.GetProperty("status").GetInt32(), stillThere.GetProperty("status").GetInt32());
        Assert.Equal(before.GetProperty("statusHistory").GetArrayLength(), stillThere.GetProperty("statusHistory").GetArrayLength());
        Assert.Equal(0m, stillThere.GetProperty("paidAmount").GetDecimal());
        Assert.Equal(0, stillThere.GetProperty("payments").GetArrayLength());
        // Its lines and history come with it, for its own business.
        Assert.Equal("Agua de A", stillThere.GetProperty("items")[0].GetProperty("productName").GetString());
        Assert.NotEqual(0, stillThere.GetProperty("statusHistory").GetArrayLength());
    }

    [SqlServerFact]
    public async Task PrivateFiles_OfAnotherBusiness_ShouldNotOpen()
    {
        Assert.Equal(HttpStatusCode.OK, (await A.Client.GetAsync($"/api/v1{A.ReceiptPath}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await B.Client.GetAsync($"/api/v1{A.ReceiptPath}")).StatusCode);
    }

    [SqlServerFact]
    public async Task AnotherBusinessesHeaderRouteOrSwitch_ShouldNotEnterIt()
    {
        // The business override header is only for the platform admin: for an owner it changes nothing.
        using var withHeader = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/products/{A.ProductId}");
        withHeader.Headers.Add("X-Tenant-Id", A.TenantId.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await B.Client.SendAsync(withHeader)).StatusCode);

        // The orders route of the other business.
        var ordersBefore = await CountOrdersAsync(A);
        AssertRefused(await B.Client.PostAsJsonAsync($"/api/v1/tenants/{A.TenantId}/orders", new
        {
            phone = "5511112222",
            defaultCountry = "MX",
            items = new[] { new { productId = A.ProductId, quantity = 1, optionIds = Array.Empty<Guid>() } }
        }));
        Assert.Equal(ordersBefore, await CountOrdersAsync(A));

        // Switching to a business without a membership in it.
        AssertRefused(await B.Client.PostAsJsonAsync("/api/v1/auth/switch-tenant", new { tenantId = A.TenantId }));

        // Its own business, its own settings.
        var mine = await B.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/my-tenant");
        Assert.Equal(B.TenantId.ToString(), mine.GetProperty("id").GetString());
    }

    [SqlServerFact]
    public async Task PlatformAdmin_InAnotherBusiness_ShouldBeRecordedInItsActivity()
    {
        var platform = await _api.SignUpAsync("plataforma");
        using var admin = await _api.SignInAsPlatformAdminAsync(platform);

        // A read (its search stays out of the record) and a change in A, by its header.
        var products = await TwoBusinessesApi.ReadAsync(await admin.SendAsync(InBusiness(A.TenantId, HttpMethod.Get, "/api/v1/products?search=Agua&pageSize=5")));
        Assert.Contains(products.GetProperty("items").EnumerateArray(), product => product.GetProperty("id").GetGuid() == A.ProductId);
        using var newCategory = InBusiness(A.TenantId, HttpMethod.Post, "/api/v1/categories");
        newCategory.Content = JsonContent.Create(new { name = "Puesta por soporte", sortOrder = 9 });
        await TwoBusinessesApi.ReadAsync(await admin.SendAsync(newCategory));
        // A business that does not exist: nothing runs.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(InBusiness(Guid.NewGuid(), HttpMethod.Get, "/api/v1/products"))).StatusCode);

        var activity = await A.Client.GetFromJsonAsync<JsonElement>($"/api/v1/audit/logs?action={SupportAccessAudit.Action}&pageSize=50");
        var entries = activity.GetProperty("items").EnumerateArray()
            .Where(entry => entry.GetProperty("performedBy").GetString() == platform.Email)
            .Select(entry => JsonDocument.Parse(entry.GetProperty("metadata").GetString()!).RootElement.Clone())
            .ToList();
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.GetProperty("Method").GetString() == "GET" && entry.GetProperty("Path").GetString() == "/api/v1/products");
        Assert.Contains(entries, entry => entry.GetProperty("Method").GetString() == "POST" && entry.GetProperty("Path").GetString() == "/api/v1/categories");
        Assert.All(entries, entry => Assert.Equal(platform.TenantId, entry.GetProperty("SessionTenantId").GetGuid()));
        Assert.DoesNotContain("Agua", activity.GetRawText());
        // Only in the business it acted on.
        Assert.DoesNotContain(platform.Email, await B.Client.GetStringAsync($"/api/v1/audit/logs?action={SupportAccessAudit.Action}&pageSize=50"));
    }

    [SqlServerFact]
    public async Task Inventory_OfAnotherBusiness_ShouldBeInvisible_AndItsStockShouldNotMove()
    {
        var supply = $"/api/v1/inventory/supplies/{A.SupplyId}";
        var location = $"/api/v1/inventory/locations/{A.LocationId}";
        var stockBefore = await StockAsync(A);

        Assert.Equal(HttpStatusCode.OK, (await A.Client.GetAsync(supply)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await B.Client.GetAsync(supply)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await B.Client.GetAsync($"/api/v1/inventory/movements/{A.MovementId}")).StatusCode);
        AssertRefused(await B.Client.PutAsJsonAsync(supply, new { id = A.SupplyId, name = "Cambiada por B", unit = 0 }));
        AssertRefused(await B.Client.PatchAsJsonAsync($"{supply}/active", new { isActive = false }));
        AssertRefused(await B.Client.PutAsJsonAsync(location, new { id = A.LocationId, name = "Cambiado por B", code = "B" }));
        AssertRefused(await B.Client.PatchAsJsonAsync($"{location}/active", new { isActive = false }));

        // Through B's movements: A's stock taken out, or B's supply put into A's warehouse.
        AssertRefused(await B.Client.PostAsJsonAsync("/api/v1/inventory/movements", new
        {
            movementType = 2,
            movementReason = 5,
            lines = new[] { new { supplyItemId = A.SupplyId, inventoryLocationId = A.LocationId, quantity = 1m } }
        }));
        AssertRefused(await B.Client.PostAsJsonAsync("/api/v1/inventory/movements", new
        {
            movementType = 1,
            movementReason = 1,
            lines = new[] { new { supplyItemId = B.SupplyId, inventoryLocationId = A.LocationId, quantity = 1m, unitCost = 1m } }
        }));

        foreach (var list in new[]
        {
            "/api/v1/inventory/supplies?pageSize=100",
            "/api/v1/inventory/locations",
            "/api/v1/inventory/stocks?pageSize=100",
            "/api/v1/inventory/movements?pageSize=100",
            $"/api/v1/inventory/kardex?supplyItemId={A.SupplyId}"
        })
        {
            var theirs = await B.Client.GetStringAsync(list);
            Assert.DoesNotContain(A.SupplyId.ToString(), theirs);
            Assert.DoesNotContain(A.LocationId.ToString(), theirs);
            Assert.DoesNotContain(A.MovementId.ToString(), theirs);
        }

        var stillThere = await A.Client.GetFromJsonAsync<JsonElement>(supply);
        Assert.Equal("Harina de A", stillThere.GetProperty("name").GetString());
        Assert.True(stillThere.GetProperty("isActive").GetBoolean());
        Assert.Equal(stockBefore, await StockAsync(A));
    }

    [SqlServerFact]
    public async Task PurchaseOrders_OfAnotherBusiness_ShouldBeInvisible_AndNotBeReceivedClosedOrCancelled()
    {
        var purchaseOrder = $"/api/v1/purchaseorders/{A.PurchaseOrderId}";
        var before = await A.Client.GetFromJsonAsync<JsonElement>(purchaseOrder);
        var stockBefore = await StockAsync(A);

        Assert.Equal(HttpStatusCode.NotFound, (await B.Client.GetAsync(purchaseOrder)).StatusCode);
        AssertRefused(await B.Client.PostAsJsonAsync($"{purchaseOrder}/receive", new
        {
            lines = new[] { new { purchaseOrderLineId = A.PurchaseOrderLineId, quantityReceived = 5m } }
        }));
        AssertRefused(await B.Client.PostAsync($"{purchaseOrder}/send", null));
        AssertRefused(await B.Client.PostAsJsonAsync($"{purchaseOrder}/close", new { reason = "Cerrada por B" }));
        AssertRefused(await B.Client.PostAsync($"{purchaseOrder}/cancel", null));

        // A's supplier or supply in an order of B.
        AssertRefused(await B.Client.PostAsJsonAsync("/api/v1/purchaseorders", new
        {
            supplierId = A.SupplierId,
            lines = new[] { new { supplyItemId = B.SupplyId, inventoryLocationId = B.LocationId, quantity = 1m, unitCost = 1m, taxPercent = 0m } }
        }));
        AssertRefused(await B.Client.PostAsJsonAsync("/api/v1/purchaseorders", new
        {
            supplierId = B.SupplierId,
            lines = new[] { new { supplyItemId = A.SupplyId, inventoryLocationId = B.LocationId, quantity = 1m, unitCost = 1m, taxPercent = 0m } }
        }));
        Assert.DoesNotContain(A.PurchaseOrderId.ToString(), await B.Client.GetStringAsync("/api/v1/purchaseorders?pageSize=100"));

        var stillThere = await A.Client.GetFromJsonAsync<JsonElement>(purchaseOrder);
        Assert.Equal(before.GetProperty("status").GetInt32(), stillThere.GetProperty("status").GetInt32());
        Assert.Equal(
            before.GetProperty("lines")[0].GetProperty("receivedQuantity").GetDecimal(),
            stillThere.GetProperty("lines")[0].GetProperty("receivedQuantity").GetDecimal());
        Assert.Equal(stockBefore, await StockAsync(A));
    }

    [SqlServerFact]
    public async Task CashSession_OfAnotherBusiness_ShouldNotCloseOrTakeMovements()
    {
        AssertRefused(await B.Client.PostAsJsonAsync($"/api/v1/cash/{A.CashSessionId}/close", new { closingAmount = 0m }));
        AssertRefused(await B.Client.PostAsJsonAsync("/api/v1/cash/transactions", new
        {
            cashSessionId = A.CashSessionId,
            transactionType = 3,
            amount = 50m
        }));
        Assert.DoesNotContain(A.CashSessionId.ToString(), await B.Client.GetStringAsync("/api/v1/cash/sessions?pageSize=100"));
        Assert.DoesNotContain(
            A.CashSessionId.ToString(),
            await B.Client.GetStringAsync($"/api/v1/cash/transactions?cashSessionId={A.CashSessionId}&pageSize=100"));

        // A's drawer: still open, with nothing taken out.
        var current = await A.Client.GetFromJsonAsync<JsonElement>("/api/v1/cash/current-session");
        Assert.Equal(A.CashSessionId, current.GetProperty("id").GetGuid());
        Assert.Equal(0, current.GetProperty("status").GetInt32());
        var movements = await A.Client.GetFromJsonAsync<JsonElement>($"/api/v1/cash/transactions?cashSessionId={A.CashSessionId}&pageSize=100");
        Assert.DoesNotContain(
            movements.GetProperty("items").EnumerateArray(),
            transaction => transaction.GetProperty("amount").GetDecimal() == 50m);
    }

    [SqlServerFact]
    public async Task Appointments_OfAnotherBusiness_ShouldBeInvisibleAndUntouchable()
    {
        var appointment = $"/api/v1/appointments/{A.AppointmentId}";
        var before = await A.Client.GetFromJsonAsync<JsonElement>(appointment);

        Assert.Equal(HttpStatusCode.NotFound, (await B.Client.GetAsync(appointment)).StatusCode);
        AssertRefused(await B.Client.PutAsJsonAsync(appointment, new
        {
            customerName = "Cambiada por B",
            customerPhone = "5599998888",
            serviceName = "Otro",
            startAt = DateTimeOffset.UtcNow.AddDays(2),
            endAt = DateTimeOffset.UtcNow.AddDays(2).AddHours(1)
        }));
        AssertRefused(await B.Client.PutAsJsonAsync($"{appointment}/status", new { newStatus = 99 }));
        Assert.DoesNotContain(A.AppointmentId.ToString(), await B.Client.GetStringAsync("/api/v1/appointments?pageSize=100"));

        var stillThere = await A.Client.GetFromJsonAsync<JsonElement>(appointment);
        Assert.Equal(before.GetProperty("status").GetInt32(), stillThere.GetProperty("status").GetInt32());
        Assert.Equal("Cliente de A", stillThere.GetProperty("customerName").GetString());
        Assert.Equal(before.GetProperty("startAt").GetDateTimeOffset(), stillThere.GetProperty("startAt").GetDateTimeOffset());
    }

    private static HttpRequestMessage InBusiness(Guid tenantId, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        return request;
    }

    private static async Task<decimal> StockAsync(Business business)
    {
        var page = await business.Client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/inventory/stocks?inventoryLocationId={business.LocationId}&pageSize=100");
        return page.GetProperty("items").EnumerateArray()
            .Single(stock => stock.GetProperty("supplyItemId").GetGuid() == business.SupplyId)
            .GetProperty("onHandQuantity").GetDecimal();
    }

    private static async Task<int> CountOrdersAsync(Business business)
    {
        var page = await business.Client.GetFromJsonAsync<JsonElement>("/api/v1/orders?pageSize=100");
        return page.GetProperty("totalCount").GetInt32();
    }

    internal static void AssertRefused(HttpResponseMessage response)
    {
        // Not found, refused or invalid: never applied.
        Assert.False(response.IsSuccessStatusCode, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} answered {(int)response.StatusCode}");
        Assert.True((int)response.StatusCode is >= 400 and < 500, $"{(int)response.StatusCode}");
    }

    internal static ProductRequest NewProduct(Guid categoryId, string name) => new(
        Id: null, CategoryId: categoryId, Name: name, PriceAmount: 15m, PriceCurrency: "MXN", SortOrder: 1,
        IsFeatured: false, IsTaxExempt: false, IsTrackable: false, NotifyCustomer: false, TaxObject: 2);

    internal sealed record ProductRequest(
        Guid? Id, Guid CategoryId, string Name, decimal PriceAmount, string PriceCurrency, int SortOrder,
        bool IsFeatured, bool IsTaxExempt, bool IsTrackable, bool NotifyCustomer, int TaxObject);
}
