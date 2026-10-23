using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Invoicing (Fase 28A, ADR 0045) through the API: a business sees, stamps, downloads and cancels only the invoices of its
/// own orders, with its own CSD and folios (the business header of an owner changes nothing); staff who manage the orders
/// stamp, but only owners and admins cancel and touch the CSD.
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantInvoicingSqlServerTests
{
    private static readonly object Receiver = new
    {
        rfc = "XIQB891116QE4",
        name = "Karla Fuente Nolasco",
        postalCode = "26015",
        fiscalRegime = "612",
        cfdiUse = "G03"
    };

    private readonly TwoBusinessesApi _api;

    public CrossTenantInvoicingSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Invoices_OfAnotherBusiness_ShouldBeInvisible_AndUntouchable()
    {
        var mine = await _api.SignUpAsync("facturas-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("facturas-b", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        await ReadyToInvoiceAsync(theirs.TenantId, "IIA040805DZ4");
        var (order, total) = await PaidOrderAsync(mine.Client, await ProductAsync(mine.Client, "Factura A"), phone: "5512345678", name: "Karla");

        var invoice = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = false }));
        var invoiceId = invoice.GetProperty("id").GetGuid();
        Assert.Equal(((int)OrderInvoiceStatus.Stamped, "F-1", total), (
            invoice.GetProperty("status").GetInt32(), invoice.GetProperty("number").GetString(), invoice.GetProperty("total").GetDecimal()));

        // The other owner, even with the header of my business, finds neither my order's invoicing nor my invoice.
        foreach (var url in new[]
        {
            $"/api/v1/invoicing/orders/{order}",
            $"/api/v1/invoicing/invoices/{invoiceId}/xml",
            $"/api/v1/invoicing/invoices/{invoiceId}/pdf"
        })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(mine, HttpMethod.Get, url))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsync($"/api/v1/invoicing/invoices/{invoiceId}/retry", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/invoices/{invoiceId}/cancel", new { motive = (int)CfdiCancellationMotive.NotCarriedOut })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(
            mine, HttpMethod.Post, $"/api/v1/invoicing/invoices/{invoiceId}/cancel", new { motive = (int)CfdiCancellationMotive.NotCarriedOut }))).StatusCode);
        Assert.Empty(_api.Invoicing.Cancellations.Where(c => c.Uuid == invoice.GetProperty("uuid").GetString()));

        // Their settings are theirs: their own CSD, untouched by removing it, and mine stays.
        var theirSettings = await TwoBusinessesApi.ReadAsync(await theirs.Client.SendAsync(AsOther(mine, HttpMethod.Get, $"/api/v1/invoicing/settings")));
        Assert.Equal("IIA040805DZ4", theirSettings.GetProperty("rfc").GetString());
        await TwoBusinessesApi.ReadAsync(await theirs.Client.SendAsync(AsOther(mine, HttpMethod.Delete, $"/api/v1/invoicing/settings/certificate")));
        Assert.True(await _api.QueryAsync(mine.TenantId, db => db.InvoicingProfiles.AnyAsync(p => p.CertificateNumber != null)));
        Assert.False(await _api.QueryAsync(theirs.TenantId, db => db.InvoicingProfiles.AnyAsync(p => p.CertificateNumber != null)));

        // My XML and PDF are mine to download; their folios start at 1.
        var xml = await mine.Client.GetAsync($"/api/v1/invoicing/invoices/{invoiceId}/xml");
        Assert.Equal(HttpStatusCode.OK, xml.StatusCode);
        Assert.Contains(invoice.GetProperty("uuid").GetString()!, await xml.Content.ReadAsStringAsync());
        Assert.True(xml.Headers.CacheControl is { Private: true, NoStore: true });
        Assert.Equal(HttpStatusCode.OK, (await mine.Client.GetAsync($"/api/v1/invoicing/invoices/{invoiceId}/pdf")).StatusCode);
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.OrderInvoices.ToListAsync()));
        Assert.Single(await _api.QueryAsync(mine.TenantId, db => db.OrderInvoices.ToListAsync()));
    }

    [SqlServerFact]
    public async Task Staff_ShouldStampWhatTheyManage_ButOnlyOwnersCancelAndTouchTheCsd()
    {
        var mine = await _api.SignUpAsync("facturas-equipo", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        var (order, _) = await PaidOrderAsync(mine.Client, await ProductAsync(mine.Client, "Factura equipo"), phone: null, name: "Mostrador");
        var cashier = await EmployeeAsync(mine, "ManageOrders", "ChargeOrders", "ManageCash");
        var stock = await EmployeeAsync(mine, "ManageInventory");

        Assert.Equal(HttpStatusCode.Forbidden, (await stock.GetAsync($"/api/v1/invoicing/orders/{order}")).StatusCode);
        Assert.True((await TwoBusinessesApi.ReadAsync(await cashier.GetAsync($"/api/v1/invoicing/orders/{order}"))).GetProperty("canInvoice").GetBoolean());
        var invoice = await TwoBusinessesApi.ReadAsync(await cashier.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = false }));
        var invoiceId = invoice.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync(
            $"/api/v1/invoicing/invoices/{invoiceId}/cancel", new { motive = (int)CfdiCancellationMotive.NotCarriedOut })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync($"/api/v1/invoicing/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.DeleteAsync($"/api/v1/invoicing/settings/certificate")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PutAsJsonAsync($"/api/v1/invoicing/settings/serie", new { serie = "X" })).StatusCode);

        var cancelling = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/invoices/{invoiceId}/cancel", new { motive = (int)CfdiCancellationMotive.NotCarriedOut }));
        Assert.Equal((int)OrderInvoiceStatus.Cancelling, cancelling.GetProperty("status").GetInt32());
        Assert.Equal(2, await _api.QueryAsync(mine.TenantId, db => db.AuditLogs.CountAsync(a => a.EntityName == nameof(OrderInvoice))));
    }

    [SqlServerFact]
    public async Task FiscalData_OfAnotherBusinesssCustomer_ShouldBeUntouchable()
    {
        var mine = await _api.SignUpAsync("facturas-cliente-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("facturas-cliente-b", TwoBusinessesApi.FoodBusiness);
        var customer = await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/customers", new { name = "Karla", phone = "5512345678" });

        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PutAsJsonAsync(
            $"/api/v1/customers/{customer}/fiscal-data", new { fiscalData = Receiver })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(
            mine, HttpMethod.Put, $"/api/v1/customers/{customer}/fiscal-data", new { fiscalData = Receiver }))).StatusCode);

        var saved = await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync(
            $"/api/v1/customers/{customer}/fiscal-data", new { fiscalData = Receiver }));
        Assert.Equal("KARLA FUENTE NOLASCO", saved.GetProperty("fiscalData").GetProperty("name").GetString());
    }

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

    /// <summary>A member of the owner's team with the permissions given, signed in with its own password.</summary>
    private async Task<HttpClient> EmployeeAsync(SignedUp owner, params string[] permissions)
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
            tenantRole = 2,
            permissions
        }));
        var client = _api.AnonymousClient();
        var signedIn = await TwoBusinessesApi.ReadAsync(await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.GetProperty("accessToken").GetString());
        return client;
    }
}
