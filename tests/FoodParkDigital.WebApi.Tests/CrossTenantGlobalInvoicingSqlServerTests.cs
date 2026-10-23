using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.Common.Utils;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The global invoice of a period (Fase 28B, ADR 0046) through the API: a business previews, stamps, downloads and cancels
/// only its own, with its own sales, CSD and folios (the business header of an owner changes nothing); only owners and
/// admins touch it; and the invoice of an order and the global invoice of its period asked at once cover the sale once,
/// on SQL Server's invoicing lock of the business.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantGlobalInvoicingSqlServerTests
{
    private const int Daily = (int)GlobalInvoicePeriodicity.Daily;

    private static readonly object Receiver = new
    {
        rfc = "XIQB891116QE4",
        name = "Karla Fuente Nolasco",
        postalCode = "26015",
        fiscalRegime = "612",
        cfdiUse = "G03"
    };

    private readonly TwoBusinessesApi _api;

    public CrossTenantGlobalInvoicingSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task GlobalInvoices_OfAnotherBusiness_ShouldBeInvisible_AndUntouchable()
    {
        var mine = await _api.SignUpAsync("global-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("global-b", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        await ReadyToInvoiceAsync(theirs.TenantId, "IIA040805DZ4");
        var product = await ProductAsync(mine.Client, "Global A");
        var (first, firstTotal) = await PaidOrderAsync(mine.Client, product, phone: null, name: "Mostrador");
        var (second, secondTotal) = await PaidOrderAsync(mine.Client, product, phone: null, name: "Mostrador");
        var (their, _) = await PaidOrderAsync(theirs.Client, await ProductAsync(theirs.Client, "Global B"), phone: null, name: "Mostrador");
        var day = await PaidYesterdayAsync(mine.TenantId, first, second);
        await PaidYesterdayAsync(theirs.TenantId, their);

        // Each preview holds only the sales of its business, even with the header of the other one.
        var draft = await DraftAsync(mine.Client, day);
        Assert.True(draft.GetProperty("isReadyToStamp").GetBoolean());
        Assert.Equal(
            new[] { first, second }.Order(),
            draft.GetProperty("sales").EnumerateArray().Select(sale => sale.GetProperty("orderId").GetGuid()).Order());
        var theirDraft = await TwoBusinessesApi.ReadAsync(await theirs.Client.SendAsync(AsOther(mine, HttpMethod.Get, DraftUrl(day))));
        Assert.Equal(their, Assert.Single(theirDraft.GetProperty("sales").EnumerateArray()).GetProperty("orderId").GetGuid());

        var invoice = await IssueAsync(mine.Client, day, draft);
        var invoiceId = invoice.GetProperty("id").GetGuid();
        Assert.Equal(((int)OrderInvoiceStatus.Stamped, "F-1", 2, firstTotal + secondTotal), (
            invoice.GetProperty("status").GetInt32(), invoice.GetProperty("number").GetString(),
            invoice.GetProperty("orderCount").GetInt32(), invoice.GetProperty("total").GetDecimal()));

        // The other owner, even with the header of my business, finds none of it nor stamps with my sales.
        foreach (var url in new[]
        {
            $"/api/v1/invoicing/global-invoices/{invoiceId}",
            $"/api/v1/invoicing/global-invoices/{invoiceId}/xml",
            $"/api/v1/invoicing/global-invoices/{invoiceId}/pdf"
        })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(mine, HttpMethod.Get, url))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsync($"/api/v1/invoicing/global-invoices/{invoiceId}/retry", null)).StatusCode);
        var cancel = new { motive = (int)CfdiCancellationMotive.ErrorsWithoutReplacement };
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/global-invoices/{invoiceId}/cancel", cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(
            mine, HttpMethod.Post, $"/api/v1/invoicing/global-invoices/{invoiceId}/cancel", cancel))).StatusCode);
        Assert.DoesNotContain(_api.Invoicing.Cancellations, c => c.Uuid == invoice.GetProperty("uuid").GetString());
        var theirList = await TwoBusinessesApi.ReadAsync(await theirs.Client.SendAsync(AsOther(mine, HttpMethod.Get, "/api/v1/invoicing/global-invoices")));
        Assert.Empty(theirList.GetProperty("items").EnumerateArray());

        // Theirs is theirs: their sale, their folio 1, and my global invoice keeps mine.
        var theirInvoice = await IssueAsync(theirs.Client, day, theirDraft);
        Assert.Equal(("F-1", 1), (theirInvoice.GetProperty("number").GetString(), theirInvoice.GetProperty("orderCount").GetInt32()));
        var detail = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/invoicing/global-invoices/{invoiceId}"));
        Assert.Equal(
            new[] { first, second }.Order(),
            detail.GetProperty("orders").EnumerateArray().Select(order => order.GetProperty("orderId").GetGuid()).Order());
        var list = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync("/api/v1/invoicing/global-invoices"));
        Assert.Equal(invoiceId, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());

        // My XML and PDF are mine to download.
        var xml = await mine.Client.GetAsync($"/api/v1/invoicing/global-invoices/{invoiceId}/xml");
        Assert.Equal(HttpStatusCode.OK, xml.StatusCode);
        Assert.Contains(invoice.GetProperty("uuid").GetString()!, await xml.Content.ReadAsStringAsync());
        Assert.True(xml.Headers.CacheControl is { Private: true, NoStore: true });
        Assert.Equal(HttpStatusCode.OK, (await mine.Client.GetAsync($"/api/v1/invoicing/global-invoices/{invoiceId}/pdf")).StatusCode);
        Assert.Equal(2, await _api.QueryAsync(mine.TenantId, db => db.GlobalInvoiceOrders.CountAsync()));
        Assert.Equal(1, await _api.QueryAsync(theirs.TenantId, db => db.GlobalInvoiceOrders.CountAsync()));
    }

    [SqlServerFact]
    public async Task OnlyOwnersAndAdmins_ShouldTouchTheGlobalInvoice()
    {
        var mine = await _api.SignUpAsync("global-equipo", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        var (order, _) = await PaidOrderAsync(mine.Client, await ProductAsync(mine.Client, "Global equipo"), phone: null, name: "Mostrador");
        var day = await PaidYesterdayAsync(mine.TenantId, order);
        // Who sells, charges and sees the reports does not stamp to the general public: that is the business's fiscal side.
        var cashier = await EmployeeAsync(mine, tenantRole: 2, "ManageOrders", "ChargeOrders", "ManageCash", "ViewReports");
        var admin = await EmployeeAsync(mine, tenantRole: 1);

        var draft = await DraftAsync(admin, day);
        var body = Issue(day, draft);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(DraftUrl(day))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync("/api/v1/invoicing/global-invoices", body)).StatusCode);
        Assert.Empty(await _api.QueryAsync(mine.TenantId, db => db.GlobalInvoices.ToListAsync()));

        var invoiceId = (await TwoBusinessesApi.ReadAsync(await admin.PostAsJsonAsync("/api/v1/invoicing/global-invoices", body)))
            .GetProperty("id").GetGuid();
        var cancel = new { motive = (int)CfdiCancellationMotive.NominativeInGlobalInvoice };
        foreach (var url in new[]
        {
            "/api/v1/invoicing/global-invoices",
            $"/api/v1/invoicing/global-invoices/{invoiceId}",
            $"/api/v1/invoicing/global-invoices/{invoiceId}/xml",
            $"/api/v1/invoicing/global-invoices/{invoiceId}/pdf"
        })
            Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsync($"/api/v1/invoicing/global-invoices/{invoiceId}/retry", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync($"/api/v1/invoicing/global-invoices/{invoiceId}/cancel", cancel)).StatusCode);

        var cancelling = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/global-invoices/{invoiceId}/cancel", cancel));
        Assert.Equal((int)OrderInvoiceStatus.Cancelling, cancelling.GetProperty("status").GetInt32());
        Assert.Equal(2, await _api.QueryAsync(mine.TenantId, db => db.AuditLogs.CountAsync(a => a.EntityName == nameof(GlobalInvoice))));
    }

    [SqlServerFact]
    public async Task AnOrderInvoiceAndTheGlobalInvoiceAtOnce_ShouldCoverTheSaleOnce()
    {
        var mine = await _api.SignUpAsync("global-carrera", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        var product = await ProductAsync(mine.Client, "Global carrera");
        var (raced, _) = await PaidOrderAsync(mine.Client, product, phone: null, name: "Mostrador");
        var (other, _) = await PaidOrderAsync(mine.Client, product, phone: null, name: "Mostrador");
        var day = await PaidYesterdayAsync(mine.TenantId, raced, other);
        var draft = await DraftAsync(mine.Client, day);
        Assert.Equal(2, draft.GetProperty("saleCount").GetInt32());

        // The customer asks for their invoice while the owner stamps the global one of yesterday. While the invoicing lock of
        // the business is held, both wait for it; the one after it finds the sale taken by the other.
        var resource = $"invoicing:{mine.TenantId:N}";
        var answers = await _api.QueryAsync(mine.TenantId, async db =>
        {
            await using var held = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"EXEC sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction'");
            var both = new[]
            {
                mine.Client.PostAsJsonAsync($"/api/v1/invoicing/orders/{raced}/invoices", new { receiver = Receiver, saveToCustomer = false }),
                mine.Client.PostAsJsonAsync("/api/v1/invoicing/global-invoices", Issue(day, draft))
            };
            await Task.Delay(TimeSpan.FromSeconds(2));
            await held.CommitAsync();
            return await Task.WhenAll(both);
        });

        Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.OK);
        var refused = Assert.Single(answers, answer => answer.StatusCode != HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var detail = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString()!;
        Assert.True(
            detail.StartsWith("Esta venta está en la factura global F-", StringComparison.Ordinal)
                || detail.StartsWith("Las ventas del periodo cambiaron", StringComparison.Ordinal),
            detail);
        Assert.Equal(1, await _api.QueryAsync(mine.TenantId, async db =>
            await db.OrderInvoices.CountAsync(i => i.OrderId == raced)
                + await db.GlobalInvoiceOrders.CountAsync(o => o.OrderId == raced && !o.Released)));
    }

    private static string DraftUrl(DateOnly day)
        => $"/api/v1/invoicing/global-invoices/draft?periodicity={Daily}&day={day:yyyy-MM-dd}";

    private static async Task<JsonElement> DraftAsync(HttpClient client, DateOnly day)
        => await TwoBusinessesApi.ReadAsync(await client.GetAsync(DraftUrl(day)));

    /// <summary>The body that stamps the global invoice of the day as the preview showed it.</summary>
    private static object Issue(DateOnly day, JsonElement draft) => new
    {
        periodicity = Daily,
        day,
        expectedSaleCount = draft.GetProperty("saleCount").GetInt32(),
        expectedTotal = draft.GetProperty("total").GetDecimal(),
        expectedFingerprint = draft.GetProperty("fingerprint").GetString()
    };

    private static async Task<JsonElement> IssueAsync(HttpClient client, DateOnly day, JsonElement draft)
        => await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync("/api/v1/invoicing/global-invoices", Issue(day, draft)));

    /// <summary>
    /// Moves the payments of the sales to noon of yesterday in the business's time zone: a day that is over, the only kind a
    /// global invoice covers.
    /// </summary>
    /// <returns>Yesterday, in the business's time zone.</returns>
    private Task<DateOnly> PaidYesterdayAsync(Guid tenantId, params Guid[] orders)
        => _api.QueryAsync(tenantId, async db =>
        {
            var zone = TenantDateTimeResolver.Resolve(await db.TenantSettings
                .Where(s => s.TenantId == tenantId)
                .Select(s => s.TimeZone)
                .SingleAsync());
            var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime).AddDays(-1);
            var noon = TenantDateTimeResolver.StartOfLocalDayUtc(yesterday.ToDateTime(TimeOnly.MinValue), zone).AddHours(12);
            foreach (var order in orders)
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE OrderPayments SET PaidAt = {noon} WHERE OrderId = {order}");
            return yesterday;
        });

    /// <summary>The business's fiscal data in Mi negocio and a CSD of its RFC in force, as if uploaded.</summary>
    private Task ReadyToInvoiceAsync(Guid tenantId, string rfc)
        => _api.QueryAsync(tenantId, async db =>
        {
            var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId);
            tenant.SetBusinessInfo(rfc, "ESCUELA KEMPER URGATE", "Comercio", "601 - General de Ley Personas Morales",
                new Address("Reforma", "1", null, "Centro", "06000", "Cuauhtémoc", "Ciudad de México"));
            var profile = InvoicingProfile.Create(tenantId);
            // Each business its own CSD: the database keeps a number for one business only.
            var number = $"3000100000{Random.Shared.NextInt64(1_000_000_000L, 10_000_000_000L)}";
            profile.UseCertificate(number, rfc, DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow.AddYears(3),
                Guid.NewGuid(), DateTimeOffset.UtcNow);
            db.InvoicingProfiles.Add(profile);
            return await db.SaveChangesAsync();
        });

    private static HttpRequestMessage AsOther(SignedUp business, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("X-Tenant-Id", business.TenantId.ToString());
        return request;
    }

    /// <summary>A member of the owner's team with the role and permissions given, signed in with its own password.</summary>
    private async Task<HttpClient> EmployeeAsync(SignedUp owner, int tenantRole, params string[] permissions)
    {
        const string password = "Secreto1";
        var email = $"equipo-{Guid.NewGuid():N}@demo.com";
        await TwoBusinessesApi.ReadAsync(await owner.Client.PostAsJsonAsync("/api/v1/employees", new
        {
            email,
            password,
            firstName = "Empleado",
            lastName = "De prueba",
            appRole = 3,
            tenantRole,
            permissions
        }));
        var client = _api.AnonymousClient();
        var signedIn = await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.GetProperty("accessToken").GetString());
        return client;
    }
}
