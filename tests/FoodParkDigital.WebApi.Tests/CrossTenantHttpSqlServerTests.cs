using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.Audit;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

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
    public async Task AccountRoutes_ShouldShowOnlyTheirOwnBusinessesAndPurchases()
    {
        // The account routes read across businesses on purpose (a person can belong to several and buy in any), so each
        // depends on its own filter: their memberships, their access and their purchases, never the other business's.
        var businesses = (await B.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/tenants"))
            .EnumerateArray()
            .Select(business => business.GetProperty("tenantId").GetGuid())
            .ToList();
        Assert.Contains(B.TenantId, businesses);
        Assert.DoesNotContain(A.TenantId, businesses);

        var access = await B.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/access");
        Assert.Equal(B.TenantId, access.GetProperty("tenantId").GetGuid());
        Assert.False(access.GetProperty("isPlatformAdmin").GetBoolean());

        // The other business has sales; this owner bought nothing anywhere.
        Assert.True(await CountOrdersAsync(A) > 0);
        var purchases = await B.Client.GetFromJsonAsync<JsonElement>("/api/v1/me/orders?pageSize=50");
        Assert.Empty(purchases.GetProperty("items").EnumerateArray());
    }

    [SqlServerFact]
    public async Task EveryBusinessesList_AndCreatingOne_ShouldBeOnlyForThePlatformAdmin()
    {
        // The list of every business and creating one belong to the platform console: an owner gets neither.
        var list = await B.Client.GetAsync("/api/v1/tenants?pageSize=100");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.DoesNotContain(A.TenantId.ToString(), await list.Content.ReadAsStringAsync());

        var slug = $"ajeno-{Guid.NewGuid():N}"[..16];
        var created = await B.Client.PostAsJsonAsync("/api/v1/tenants", new
        {
            name = "Negocio ajeno",
            slug,
            businessType = 1,
            contactEmail = $"{slug}@demo.com"
        });
        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
        Assert.False(await _api.QueryAsync(B.TenantId, db => db.Tenants.AnyAsync(tenant => tenant.Slug == slug)));
    }

    [SqlServerFact]
    public async Task PrivateFiles_OfAnotherBusiness_ShouldNotOpen()
    {
        // A private file is only served by the files endpoint, to the members of its business.
        var receipt = $"/api/v1{A.ReceiptPath}";
        Assert.StartsWith("/api/v1/files/", receipt);
        Assert.Equal(HttpStatusCode.OK, (await A.Client.GetAsync(receipt)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await B.Client.GetAsync(receipt)).StatusCode);
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
    public async Task BusinessSettings_WithTheHeaderOfAnother_ShouldReadAndChangeOnlyTheirOwn()
    {
        var aBefore = await A.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/my-tenant");
        var bBefore = await B.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/my-tenant");
        // B sets what A does not have, so a change that reached A would show.
        var pricesIncludeTax = !aBefore.GetProperty("pricesIncludeTax").GetBoolean();
        var stockPolicy = aBefore.GetProperty("insufficientStockPolicy").GetInt32() == 0 ? 1 : 0;
        var menuMode = aBefore.GetProperty("onlineMenuMode").GetInt32() == 0 ? 1 : 0;
        var theme = aBefore.GetProperty("theme").GetInt32() == 0 ? 3 : 0;
        try
        {
            // The business override header is only for the platform admin: an owner's changes stay in their business.
            using var policy = InBusiness(A.TenantId, HttpMethod.Put, "/api/v1/auth/my-tenant/sales-policy");
            policy.Content = JsonContent.Create(SalesPolicy(bBefore, pricesIncludeTax, stockPolicy));
            await TwoBusinessesApi.ReadAsync(await B.Client.SendAsync(policy));
            using var menu = InBusiness(A.TenantId, HttpMethod.Put, "/api/v1/auth/my-tenant/online-menu");
            menu.Content = JsonContent.Create(new { mode = menuMode });
            await TwoBusinessesApi.ReadAsync(await B.Client.SendAsync(menu));
            using var colors = InBusiness(A.TenantId, HttpMethod.Put, "/api/v1/auth/my-tenant/theme");
            colors.Content = JsonContent.Create(new { theme });
            await TwoBusinessesApi.ReadAsync(await B.Client.SendAsync(colors));
            using var hours = InBusiness(A.TenantId, HttpMethod.Put, "/api/v1/auth/my-tenant/opening-hours");
            hours.Content = JsonContent.Create(new { intervals = new[] { new { day = 1, opens = "09:00", closes = "20:00" } } });
            await TwoBusinessesApi.ReadAsync(await B.Client.SendAsync(hours));

            var aAfter = await A.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/my-tenant");
            var bAfter = await B.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/my-tenant");
            foreach (var setting in new[] { "pricesIncludeTax", "insufficientStockPolicy", "onlineMenuMode", "theme", "openingHours" })
                Assert.Equal(aBefore.GetProperty(setting).GetRawText(), aAfter.GetProperty(setting).GetRawText());
            Assert.Equal(pricesIncludeTax, bAfter.GetProperty("pricesIncludeTax").GetBoolean());
            Assert.Equal(stockPolicy, bAfter.GetProperty("insufficientStockPolicy").GetInt32());
            Assert.Equal(menuMode, bAfter.GetProperty("onlineMenuMode").GetInt32());
            Assert.Equal(theme, bAfter.GetProperty("theme").GetInt32());
            var bHours = Assert.Single(bAfter.GetProperty("openingHours").EnumerateArray());
            Assert.Equal("09:00", bHours.GetProperty("opens").GetString());

            // The guided setup and the plan it shows are B's as well.
            var setup = await TwoBusinessesApi.ReadAsync(await B.Client.SendAsync(InBusiness(A.TenantId, HttpMethod.Get, "/api/v1/auth/my-tenant/setup")));
            Assert.Equal(pricesIncludeTax, setup.GetProperty("pricesIncludeTax").GetBoolean());
            var subscription = await TwoBusinessesApi.ReadAsync(await B.Client.SendAsync(InBusiness(A.TenantId, HttpMethod.Get, "/api/v1/auth/my-tenant/subscription")));
            Assert.Equal(await B.Client.GetStringAsync("/api/v1/auth/my-tenant/subscription"), subscription.GetRawText());
        }
        finally
        {
            // The other tests of the suite sell in B with its settings.
            await TwoBusinessesApi.ReadAsync(await B.Client.PutAsJsonAsync("/api/v1/auth/my-tenant/sales-policy",
                SalesPolicy(bBefore, bBefore.GetProperty("pricesIncludeTax").GetBoolean(), bBefore.GetProperty("insufficientStockPolicy").GetInt32())));
            await TwoBusinessesApi.ReadAsync(await B.Client.PutAsJsonAsync("/api/v1/auth/my-tenant/online-menu",
                new { mode = bBefore.GetProperty("onlineMenuMode").GetInt32() }));
            await TwoBusinessesApi.ReadAsync(await B.Client.PutAsJsonAsync("/api/v1/auth/my-tenant/theme",
                new { theme = bBefore.GetProperty("theme").GetInt32() }));
            await TwoBusinessesApi.ReadAsync(await B.Client.PutAsJsonAsync("/api/v1/auth/my-tenant/opening-hours",
                new { intervals = bBefore.GetProperty("openingHours").ValueKind == JsonValueKind.Array ? bBefore.GetProperty("openingHours") : default(JsonElement?) }));
        }
    }

    [SqlServerFact]
    public async Task SpecialDays_WithTheHeaderOfAnother_ShouldReadAndChangeOnlyTheirOwn()
    {
        const string path = "/api/v1/auth/my-tenant/special-days";
        var today = DateOnly.Parse((await A.Client.GetFromJsonAsync<JsonElement>(path)).GetProperty("today").GetString()!);
        // Far ahead: the public menu tests of the suite close A today.
        var date = today.AddDays(300);
        Guid? aPeriod = null;
        Guid? bPeriod = null;
        try
        {
            aPeriod = PeriodOn(await TwoBusinessesApi.ReadAsync(await A.Client.PostAsJsonAsync(path, new { from = date, kind = 0, reason = "Inventario de A" })), date).Id;

            // The business override header is only for the platform admin: an owner's special day stays in their business.
            using var add = InBusiness(A.TenantId, HttpMethod.Post, path);
            add.Content = JsonContent.Create(new { from = date, kind = 0, reason = "Inventario de B" });
            bPeriod = PeriodOn(await TwoBusinessesApi.ReadAsync(await B.Client.SendAsync(add)), date).Id;
            var bList = await TwoBusinessesApi.ReadAsync(await B.Client.SendAsync(InBusiness(A.TenantId, HttpMethod.Get, path)));
            Assert.Equal("Inventario de B", PeriodOn(bList, date).Reason);
            Assert.DoesNotContain("Inventario de A", bList.GetRawText());

            // A's period by its id, with A's header: as if it did not exist.
            using var change = InBusiness(A.TenantId, HttpMethod.Put, $"{path}/{aPeriod}");
            change.Content = JsonContent.Create(new { revision = 1, from = date, kind = 0, reason = "Cambiado por B" });
            Assert.Equal(HttpStatusCode.NotFound, (await B.Client.SendAsync(change)).StatusCode);
            using var remove = InBusiness(A.TenantId, HttpMethod.Delete, $"{path}/{aPeriod}?revision=1");
            Assert.Equal(HttpStatusCode.NotFound, (await B.Client.SendAsync(remove)).StatusCode);

            var aPeriodAfter = PeriodOn(await A.Client.GetFromJsonAsync<JsonElement>(path), date);
            Assert.Equal((aPeriod.Value, "Inventario de A", 1), (aPeriodAfter.Id, aPeriodAfter.Reason, aPeriodAfter.Revision));

            // In its own business, a change based on another version is a conflict to reload.
            var stale = await A.Client.PutAsJsonAsync($"{path}/{aPeriod}", new { revision = 2, from = date, kind = 0, reason = "Otra versión" });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("concurrency_conflict", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        finally
        {
            if (aPeriod is { } a)
                await TwoBusinessesApi.ReadAsync(await A.Client.DeleteAsync($"{path}/{a}?revision={PeriodOn(await A.Client.GetFromJsonAsync<JsonElement>(path), date).Revision}"));
            if (bPeriod is { } b)
                await TwoBusinessesApi.ReadAsync(await B.Client.DeleteAsync($"{path}/{b}?revision={PeriodOn(await B.Client.GetFromJsonAsync<JsonElement>(path), date).Revision}"));
        }
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

    [SqlServerFact]
    public async Task RefundsAndVoids_OfAnotherBusinessesOrder_ShouldBeRefused()
    {
        // Paid sales, so there is money to give back.
        var mine = await _api.SignUpAsync("reembolsos-a");
        var theirs = await _api.SignUpAsync("reembolsos-b");
        var (order, _) = await PaidOrderAsync(mine.Client, await ProductAsync(mine.Client, "Corte de reembolsos A"), phone: null, name: "Mostrador");
        var (theirOrder, _) = await PaidOrderAsync(theirs.Client, await ProductAsync(theirs.Client, "Corte de reembolsos B"), phone: null, name: "Mostrador");
        var path = $"/api/v1/orders/{order}";
        var before = await mine.Client.GetFromJsonAsync<JsonElement>(path);

        AssertRefused(await theirs.Client.PostAsJsonAsync($"{path}/refunds", Refund(1m)));
        AssertRefused(await theirs.Client.PostAsJsonAsync($"{path}/void", new { reason = "Anulada por B" }));

        // Their own money goes back.
        await TwoBusinessesApi.ReadAsync(await theirs.Client.PostAsJsonAsync($"/api/v1/orders/{theirOrder}/refunds", Refund(1m)));
        await TwoBusinessesApi.ReadAsync(await theirs.Client.PostAsJsonAsync($"/api/v1/orders/{theirOrder}/void", new { reason = "Anulada por B" }));

        var stillThere = await mine.Client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(before.GetRawText(), stillThere.GetRawText());
    }

    [SqlServerFact]
    public async Task Recipes_ShouldNeverUseAnotherBusinesssProductsSuppliesOrWarehouses()
    {
        // Every sale consumes the supplies of its recipe: a recipe with another business's supply or warehouse would
        // move that business's stock.
        var mine = await RecipeKitAsync("recetas-a");
        var theirs = await RecipeKitAsync("recetas-b");
        var recipe = $"/api/v1/inventory/recipes/products/{mine.Product}";
        var theirRecipe = $"/api/v1/inventory/recipes/products/{theirs.Product}";
        await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync(recipe, Recipe(mine.Supply, mine.Location)));
        var before = await mine.Client.GetFromJsonAsync<JsonElement>(recipe);

        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync(recipe)).StatusCode);
        AssertRefused(await theirs.Client.PutAsJsonAsync(recipe, Recipe(theirs.Supply, theirs.Location)));
        // Refused before saving: no line of theirs points at the other business's product.
        Assert.False(await _api.QueryAsync(theirs.TenantId, db => db.ProductSupplyRequirements.AnyAsync(r => r.ProductId == mine.Product)));
        // Their own product, with the other's supply or warehouse.
        AssertRefused(await theirs.Client.PutAsJsonAsync(theirRecipe, Recipe(mine.Supply, theirs.Location)));
        AssertRefused(await theirs.Client.PutAsJsonAsync(theirRecipe, Recipe(theirs.Supply, mine.Location)));
        Assert.Empty((await theirs.Client.GetFromJsonAsync<JsonElement>(theirRecipe)).GetProperty("lines").EnumerateArray());

        // Their own recipe, with their own supply and warehouse.
        var saved = await TwoBusinessesApi.ReadAsync(await theirs.Client.PutAsJsonAsync(theirRecipe, Recipe(theirs.Supply, theirs.Location)));
        Assert.Equal(theirs.Supply, Assert.Single(saved.GetProperty("lines").EnumerateArray()).GetProperty("supplyItemId").GetGuid());

        var stillThere = await mine.Client.GetFromJsonAsync<JsonElement>(recipe);
        Assert.Equal(before.GetRawText(), stillThere.GetRawText());
    }

    /// <summary>A business of its own with a product, a supply and its default warehouse.</summary>
    private async Task<RecipeKit> RecipeKitAsync(string name)
    {
        var owner = await _api.SignUpAsync(name);
        var product = await ProductAsync(owner.Client, $"Pastel de {name}");
        var supply = await TwoBusinessesApi.IdAsync(owner.Client, "/api/v1/inventory/supplies", new { name = $"Harina de {name}", unit = 0, defaultCost = 10m });
        var locations = await owner.Client.GetFromJsonAsync<JsonElement>("/api/v1/inventory/locations");
        var location = locations.EnumerateArray().Single(l => l.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid();
        return new RecipeKit(owner.TenantId, owner.Client, product, supply, location);
    }

    private sealed record RecipeKit(Guid TenantId, HttpClient Client, Guid Product, Guid Supply, Guid Location);

    private static object Recipe(Guid supply, Guid location) => new
    {
        lines = new[] { new { supplyItemId = supply, inventoryLocationId = location, quantity = 1m, wastePercent = 0m } }
    };

    private static object Refund(decimal amount) => new { amount, method = 1, reason = "Devolución", restockItems = false };

    private static object SalesPolicy(JsonElement profile, bool pricesIncludeTax, int insufficientStockPolicy) => new
    {
        pricesIncludeTax,
        defaultTaxRate = profile.GetProperty("defaultTaxRate").GetDecimal(),
        autoCloseCounterSales = profile.GetProperty("autoCloseCounterSales").GetBoolean(),
        orderNotificationPolicy = profile.GetProperty("orderNotificationPolicy").GetInt32(),
        insufficientStockPolicy
    };

    /// <summary>The upcoming special day on a date, from the list of special days.</summary>
    private static (Guid Id, string? Reason, int Revision) PeriodOn(JsonElement specialDays, DateOnly date)
    {
        var period = Assert.Single(specialDays.GetProperty("upcoming").EnumerateArray(),
            day => day.GetProperty("from").GetString() == date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return (period.GetProperty("id").GetGuid(), period.GetProperty("reason").GetString(), period.GetProperty("revision").GetInt32());
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
