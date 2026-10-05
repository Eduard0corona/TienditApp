using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Domain.Identity.Entities;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Two businesses on the real API (the whole pipeline in process, SQL Server): the owner of one tries to read, change
/// and delete the other's records by their ids, with the other's header, route and files. Every attempt fails as if
/// the record did not exist, and nothing of the other business changes. Runs where TIENDITAPP_TEST_SQLSERVER names a
/// server (the Migrations job of CI).
/// </summary>
public sealed class CrossTenantHttpSqlServerTests : IClassFixture<TwoBusinessesApi>
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

    private static async Task<int> CountOrdersAsync(Business business)
    {
        var page = await business.Client.GetFromJsonAsync<JsonElement>("/api/v1/orders?pageSize=100");
        return page.GetProperty("totalCount").GetInt32();
    }

    private static void AssertRefused(HttpResponseMessage response)
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

/// <summary>A business registered through the API, its owner's client and the records it created.</summary>
public sealed record Business(
    Guid TenantId,
    HttpClient Client,
    Guid CategoryId,
    Guid ProductId,
    Guid CustomerId,
    Guid SupplierId,
    Guid OrderId,
    string ReceiptPath);

/// <summary>The API in process on a throwaway SQL Server database, with two businesses registered through it.</summary>
public sealed class TwoBusinessesApi : IAsyncLifetime
{
    private readonly string _privateFiles = Path.Combine(Path.GetTempPath(), "fpd-http-tests", Guid.NewGuid().ToString("N"));
    private SqlServerTestDatabase? _database;
    private WebApplicationFactory<Program>? _factory;

    public Business A { get; private set; } = null!;
    public Business B { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerTestDatabase.ConnectionVariable)))
            return;

        _database = await SqlServerTestDatabase.CreateAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            // Development: no secrets to configure (the signing key is generated) and no rate limit on sign-ups.
            host.UseEnvironment("Development");
            host.UseSetting("ConnectionStrings:DefaultConnection", _database.ConnectionString);
            host.UseSetting("Database:AutoMigrateOnStartup", "false");
            host.UseSetting("Database:SeedOnStartup", "false");
            host.UseSetting("BackgroundJobs:Enabled", "false");
            host.UseSetting("FileStorage:Provider", "Local");
            host.UseSetting("FileStorage:Local:PrivateRootPath", _privateFiles);
            // appsettings.json carries placeholder SMTP and SMS settings: without a host or an account, sign-up only
            // logs what it would send.
            host.UseSetting("EmailSettings:Smtp:Host", "");
            host.UseSetting("SmsSettings:AccountSid", "");
        });

        // The roles the seed of a new database creates (accounts are registered with them).
        using (var scope = _factory.Services.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            foreach (var role in new[] { "SUPER_ADMIN", "TENANT_ADMIN", "STAFF", "KITCHEN", "CUSTOMER" })
                await roles.CreateAsync(new ApplicationRole(role));
        }

        A = await RegisterAsync("a");
        B = await RegisterAsync("b");
    }

    public async Task DisposeAsync()
    {
        A?.Client.Dispose();
        B?.Client.Dispose();
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_database is not null)
            await _database.DisposeAsync();
        if (Directory.Exists(_privateFiles))
            Directory.Delete(_privateFiles, recursive: true);
    }

    private async Task<Business> RegisterAsync(string name)
    {
        var client = _factory!.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var registered = await ReadAsync(await client.PostAsJsonAsync("/api/v1/auth/register/business", new
        {
            email = $"dueno-{name}-{suffix}@demo.com",
            password = "Secreto1",
            firstName = "Dueño",
            lastName = name.ToUpperInvariant(),
            phoneNumber = "+52155" + Random.Shared.Next(10000000, 99999999),
            businessName = $"Negocio {name.ToUpperInvariant()}",
            businessType = 1
        }));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registered.GetProperty("accessToken").GetString());
        var tenantId = registered.GetProperty("user").GetProperty("tenantId").GetGuid();

        var category = await IdAsync(client, "/api/v1/categories", new { name = "Bebidas", sortOrder = 1 });
        var product = await IdAsync(client, "/api/v1/products", CrossTenantHttpSqlServerTests.NewProduct(category, $"Agua de {name.ToUpperInvariant()}"));
        var customer = await IdAsync(client, "/api/v1/customers", new { name = $"Cliente de {name.ToUpperInvariant()}", phone = "5511112222" });
        var supplier = await IdAsync(client, "/api/v1/suppliers", new { name = $"Proveedor de {name.ToUpperInvariant()}" });
        var order = await IdAsync(client, "/api/v1/orders", new
        {
            orderType = 0,
            channel = 0,
            createdByBusiness = true,
            openTab = false,
            customerName = "Mostrador",
            items = new[] { new { productId = product, quantity = 1, optionIds = Array.Empty<Guid>() } },
            payments = Array.Empty<object>()
        });

        using var receipt = new MultipartFormDataContent();
        var png = new ByteArrayContent(TinyPng);
        png.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        receipt.Add(png, "file", "recibo.png");
        var uploaded = await ReadAsync(await client.PostAsync("/api/v1/orders/uploads/payment-evidence", receipt));

        return new Business(tenantId, client, category, product, customer, supplier, order, uploaded.GetProperty("url").GetString()!);
    }

    private static async Task<Guid> IdAsync(HttpClient client, string path, object body)
        => (await ReadAsync(await client.PostAsJsonAsync(path, body))).GetProperty("id").GetGuid();

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.RequestMessage?.RequestUri} answered {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    // A 1x1 PNG: the upload validates the real content and re-encodes it.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
