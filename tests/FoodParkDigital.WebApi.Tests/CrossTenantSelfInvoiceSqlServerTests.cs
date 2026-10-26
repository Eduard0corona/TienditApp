using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Self-invoicing (Fase 28F, ADR 0050) through the API: the folio and code of one business's ticket mean nothing at another
/// business's address, answered as any ticket that is not there; the invoice is made in the ticket's own business, with its
/// folios, and its copy goes only to whoever knows its RFC; and each business turns self-invoicing on and reads its tickets'
/// codes for itself only (owners and admins turn it on; whoever manages the orders prints the code).
/// </summary>
[Collection(TwoBusinessesCollection.Name)]
public sealed class CrossTenantSelfInvoiceSqlServerTests
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

    public CrossTenantSelfInvoiceSqlServerTests(TwoBusinessesApi api)
    {
        _api = api;
    }

    [SqlServerFact]
    public async Task Ticket_ShouldBeInvoicedOnlyAtItsBusinessesAddress_AndItsCopySentOnlyWithItsRfc()
    {
        var mine = await _api.SignUpAsync("autofactura-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("autofactura-b", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        await ReadyToInvoiceAsync(theirs.TenantId, "IIA040805DZ4");
        var (order, total) = await PaidOrderAsync(mine.Client, await ProductAsync(mine.Client, "Autofactura A"), phone: null, name: "Karla");
        // Their ticket with the same folio as mine.
        await PaidOrderAsync(theirs.Client, await ProductAsync(theirs.Client, "Autofactura B"), phone: null, name: "Beto");

        // Off, the ticket prints no code; on, it does.
        Assert.Equal(HttpStatusCode.NoContent, (await mine.Client.GetAsync($"/api/v1/invoicing/orders/{order}/self-invoice")).StatusCode);
        var on = await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync("/api/v1/invoicing/settings/self-invoicing", new { on = true }));
        Assert.True(on.GetProperty("selfInvoicing").GetBoolean());
        await TwoBusinessesApi.ReadAsync(await theirs.Client.PutAsJsonAsync("/api/v1/invoicing/settings/self-invoicing", new { on = true }));
        var printed = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/invoicing/orders/{order}/self-invoice"));
        var (folio, code, path) = (printed.GetProperty("orderNumber").GetString()!, printed.GetProperty("code").GetString()!,
            printed.GetProperty("path").GetString()!);
        var (mySlug, theirSlug) = (await SlugAsync(mine.TenantId), await SlugAsync(theirs.TenantId));
        Assert.Equal(($"/m/{mySlug}/facturar", "ORD-000001"), (path, folio));

        // The other owner, even with the header of my business, does not read my ticket's code.
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync($"/api/v1/invoicing/orders/{order}/self-invoice")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(
            AsOther(mine, HttpMethod.Get, $"/api/v1/invoicing/orders/{order}/self-invoice"))).StatusCode);

        // At their address my folio and code are a ticket that is not there, the same as a code typed wrong.
        using var customer = _api.AnonymousClient();
        var ticket = new { folio, code };
        var notThere = await customer.PostAsJsonAsync($"/api/v1/public/self-invoice/{theirSlug}/ticket", ticket);
        Assert.Equal(HttpStatusCode.NotFound, notThere.StatusCode);
        Assert.Equal(SelfInvoiceMessages.NotFound, await DetailAsync(notThere));
        var typedWrong = await customer.PostAsJsonAsync($"/api/v1/public/self-invoice/{mySlug}/ticket", new { folio, code = "AAAAA-AAAAA" });
        Assert.Equal((HttpStatusCode.NotFound, SelfInvoiceMessages.NotFound), (typedWrong.StatusCode, await DetailAsync(typedWrong)));
        Assert.Equal(HttpStatusCode.NotFound, (await customer.PostAsJsonAsync(
            $"/api/v1/public/self-invoice/{theirSlug}/invoice", new { folio, code, receiver = Receiver, email = "karla@ejemplo.mx" })).StatusCode);
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.OrderInvoices.ToListAsync()));

        // At mine it is my ticket, invoiced in my business with my folios.
        var page = await TwoBusinessesApi.ReadAsync(await customer.GetAsync($"/api/v1/public/self-invoice/{mySlug}"));
        Assert.True(page.GetProperty("available").GetBoolean());
        var found = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync($"/api/v1/public/self-invoice/{mySlug}/ticket", ticket));
        Assert.Equal(((int)SelfInvoiceTicketStatus.CanInvoice, total), (found.GetProperty("status").GetInt32(), found.GetProperty("total").GetDecimal()));
        var karla = $"karla.{Guid.NewGuid():N}@ejemplo.mx";
        var invoiced = await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync(
            $"/api/v1/public/self-invoice/{mySlug}/invoice", new { folio, code, receiver = Receiver, email = karla }));
        Assert.Equal(((int)SelfInvoiceOutcome.Stamped, "F-1", karla), (
            invoiced.GetProperty("outcome").GetInt32(), invoiced.GetProperty("invoiceNumber").GetString(), invoiced.GetProperty("email").GetString()));
        var invoice = await _api.QueryAsync(mine.TenantId, db => db.OrderInvoices.SingleAsync());
        Assert.Equal((order, OrderInvoice.SelfInvoiceRequester), (invoice.OrderId, invoice.RequestedByUserId));
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.OrderInvoices.ToListAsync()));
        await _api.DispatchOutboxAsync();
        Assert.Single(_api.DocumentsTo(karla));
        var shown = (await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/invoicing/orders/{order}"))).GetProperty("invoices")[0];
        Assert.True(shown.GetProperty("requestedByCustomer").GetBoolean());

        // Its copy goes again only with its RFC, and never from their address.
        var other = $"otro.{Guid.NewGuid():N}@ejemplo.mx";
        Assert.Equal(HttpStatusCode.NotFound, (await customer.PostAsJsonAsync(
            $"/api/v1/public/self-invoice/{theirSlug}/resend", new { folio, code, rfc = "XIQB891116QE4", email = other })).StatusCode);
        var otherRfc = await customer.PostAsJsonAsync(
            $"/api/v1/public/self-invoice/{mySlug}/resend", new { folio, code, rfc = "EKU9003173C9", email = other });
        Assert.Equal((HttpStatusCode.BadRequest, "Ese RFC no es el de la factura de este ticket."), (otherRfc.StatusCode, await DetailAsync(otherRfc)));
        await TwoBusinessesApi.ReadAsync(await customer.PostAsJsonAsync(
            $"/api/v1/public/self-invoice/{mySlug}/resend", new { folio, code, rfc = "xiqb891116qe4", email = other }));
        await _api.DispatchOutboxAsync();
        Assert.Single(_api.DocumentsTo(other));
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.CfdiEmails.ToListAsync()));
    }

    [SqlServerFact]
    public async Task SelfInvoicing_ShouldBeEachBusinesssOwn_TurnedOnByOwnersAndPrintedByWhoManagesTheOrders()
    {
        var mine = await _api.SignUpAsync("autofactura-c", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("autofactura-d", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        await ReadyToInvoiceAsync(theirs.TenantId, "IIA040805DZ4");
        var (order, _) = await PaidOrderAsync(mine.Client, await ProductAsync(mine.Client, "Autofactura C"), phone: null, name: "Karla");

        // The other owner, with the header of my business, turns on their own.
        await TwoBusinessesApi.ReadAsync(await theirs.Client.SendAsync(
            AsOther(mine, HttpMethod.Put, "/api/v1/invoicing/settings/self-invoicing", new { on = true })));
        Assert.True(await _api.QueryAsync(theirs.TenantId, db => db.InvoicingProfiles.Select(p => p.SelfInvoicing).SingleAsync()));
        Assert.False(await _api.QueryAsync(mine.TenantId, db => db.InvoicingProfiles.Select(p => p.SelfInvoicing).SingleAsync()));
        using var customer = _api.AnonymousClient();
        Assert.False((await TwoBusinessesApi.ReadAsync(await customer.GetAsync($"/api/v1/public/self-invoice/{await SlugAsync(mine.TenantId)}")))
            .GetProperty("available").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await customer.GetAsync("/api/v1/public/self-invoice/no-existe-este-negocio")).StatusCode);

        // Whoever manages the orders prints the code but does not turn it on or off.
        await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync("/api/v1/invoicing/settings/self-invoicing", new { on = true }));
        var cashier = await EmployeeAsync(mine, "ManageOrders");
        var printed = await TwoBusinessesApi.ReadAsync(await cashier.GetAsync($"/api/v1/invoicing/orders/{order}/self-invoice"));
        Assert.Matches("^[0-9A-Z]{5}-[0-9A-Z]{5}$", printed.GetProperty("code").GetString()!);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PutAsJsonAsync("/api/v1/invoicing/settings/self-invoicing", new { on = false })).StatusCode);
        Assert.True(await _api.QueryAsync(mine.TenantId, db => db.InvoicingProfiles.Select(p => p.SelfInvoicing).SingleAsync()));
        var waiter = await EmployeeAsync(mine, "ManageCatalog");
        Assert.Equal(HttpStatusCode.Forbidden, (await waiter.GetAsync($"/api/v1/invoicing/orders/{order}/self-invoice")).StatusCode);
    }

    private Task<string> SlugAsync(Guid tenantId)
        => _api.QueryAsync(tenantId, db => db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Slug).SingleAsync());

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString();

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
