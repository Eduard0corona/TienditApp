using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using static FoodParkDigital.WebApi.Tests.CrossTenantManagementSqlServerTests;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// Invoicing (Fase 28A, ADR 0045) through the API: a business sees, stamps, downloads and cancels only the invoices of its
/// own orders, with its own CSD and folios (the business header of an owner changes nothing); staff who manage the orders
/// stamp and send them by email (Fase 28E), but only owners and admins cancel and touch the CSD, and only who manages
/// customers keeps their fiscal data; and the same order invoiced by two people at once is stamped once, on the invoicing
/// lock of the business (Fase 28B).
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
    public async Task PdfLogo_ShouldBeTheOneTheBusinessUploaded_AndNoneForOneWithout()
    {
        const string path = "/api/v1/auth/my-tenant/logo";
        var mine = await _api.SignUpAsync("logo-pdf-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("logo-pdf-b", TwoBusinessesApi.FoodBusiness);
        var logos = _api.CfdiPdfLogos;
        try
        {
            using var form = new MultipartFormDataContent();
            var png = new ByteArrayContent(TwoBusinessesApi.TinyPng);
            png.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(png, "file", "logo.png");
            Assert.Equal(HttpStatusCode.OK, (await mine.Client.PostAsync(path, form)).StatusCode);

            // The logo of Configuración › Mi negocio, read from the API's storage as PNG: only for its business.
            var logo = await logos.PngAsync(mine.TenantId, CancellationToken.None);
            Assert.NotNull(logo);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, logo[..4]);
            Assert.Null(await logos.PngAsync(theirs.TenantId, CancellationToken.None));
        }
        finally
        {
            // The pictures go to the API's public folder: the suite leaves nothing behind.
            Assert.Equal(HttpStatusCode.NoContent, (await mine.Client.DeleteAsync(path)).StatusCode);
        }

        // Without a logo, the PDF goes without one.
        Assert.Null(await logos.PngAsync(mine.TenantId, CancellationToken.None));
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
    public async Task Substitution_ShouldReplaceOnlyMyInvoice_AndCancelTheOldOneWithRelation()
    {
        var mine = await _api.SignUpAsync("sustituir-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("sustituir-b", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        await ReadyToInvoiceAsync(theirs.TenantId, "IIA040805DZ4");
        var (order, _) = await PaidOrderAsync(mine.Client, await ProductAsync(mine.Client, "Sustituir A"), phone: "5512345678", name: "Karla");
        var invoice = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = false }));
        var invoiceId = invoice.GetProperty("id").GetGuid();
        var uuid = invoice.GetProperty("uuid").GetString()!;
        var corrected = new { rfc = "XIQB891116QE4", name = "Karla Fuente Nolasco", postalCode = "26015", fiscalRegime = "612", cfdiUse = "S01" };

        // The other owner, even with the header of my business, can neither replace it nor ask for its cancellation.
        foreach (var (url, body) in new (string, object?)[]
        {
            ($"/api/v1/invoicing/invoices/{invoiceId}/substitute", new { receiver = corrected, saveToCustomer = false }),
            ($"/api/v1/invoicing/invoices/{invoiceId}/cancel-replaced", null)
        })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsJsonAsync(url, body)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(mine, HttpMethod.Post, url, body))).StatusCode);
        }
        Assert.Single(await _api.QueryAsync(mine.TenantId, db => db.OrderInvoices.ToListAsync()));

        // I replace it on SQL Server: the old one stops counting as the order's live invoice before the new one is saved, and
        // once the new one is stamped the old one is cancelled with motive 01.
        var replacement = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/invoices/{invoiceId}/substitute", new { receiver = corrected, saveToCustomer = false }));
        Assert.Equal(("F-2", (int)OrderInvoiceStatus.Stamped, invoiceId), (
            replacement.GetProperty("number").GetString(), replacement.GetProperty("status").GetInt32(),
            replacement.GetProperty("replacesInvoiceId").GetGuid()));
        Assert.Contains(_api.Invoicing.Cancellations, c => c.Uuid == uuid);
        var invoicing = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/invoicing/orders/{order}"));
        var old = invoicing.GetProperty("invoices")[1];
        Assert.Equal(((int)OrderInvoiceStatus.Cancelling, (int)CfdiCancellationMotive.ErrorsWithReplacement, "F-2", false), (
            old.GetProperty("status").GetInt32(), old.GetProperty("cancellationMotive").GetInt32(), old.GetProperty("replacedByNumber").GetString(),
            old.GetProperty("awaitsReplacedCancellation").GetBoolean()));
        // Asking again changes nothing; the other business has no invoices.
        await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsync($"/api/v1/invoicing/invoices/{invoiceId}/cancel-replaced", null));
        Assert.Single(_api.Invoicing.Cancellations, c => c.Uuid == uuid);
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.OrderInvoices.ToListAsync()));
    }

    [SqlServerFact]
    public async Task RefusedReplacement_ShouldGiveTheOrderItsInvoiceBack_WhicheverKeyGoesFirst()
    {
        var mine = await _api.SignUpAsync("sustituir-rechazo", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        var product = await ProductAsync(mine.Client, "Sustituir rechazo");
        var corrected = new { rfc = "XIQB891116QE4", name = "Karla Fuente Nolasco", postalCode = "26015", fiscalRegime = "612", cfdiUse = "S01" };

        // The refused replacement leaves the order's live invoices and the original comes back. SQL Server checks the index of
        // one live invoice per order statement by statement, and one save orders its updates by key: it has to work with the
        // replacement's key before and after the original's. Both keys are random, so each try is a new order with its own
        // original: against one original for all the tries, an original whose key fell near either end left every replacement
        // on the same side (about one run in 20), while a new pair of keys falls either way as a coin does (one side only in
        // all 40 tries is less than one run in 10^11).
        var sides = new HashSet<int>();
        for (var attempt = 0; attempt < 40 && sides.Count < 2; attempt++)
        {
            var (order, _) = await PaidOrderAsync(mine.Client, product, phone: null, name: "Mostrador");
            var invoiceId = (await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
                $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = false }))).GetProperty("id").GetGuid();
            _api.Invoicing.RefuseNextStamp = "CFDI40147 - El código postal del receptor no corresponde al RFC.";
            var refused = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
                $"/api/v1/invoicing/invoices/{invoiceId}/substitute", new { receiver = corrected, saveToCustomer = false }));
            // It replaces nothing: the answer says so, as the order's invoices do.
            Assert.Equal(((int)OrderInvoiceStatus.Refused, JsonValueKind.Null), (
                refused.GetProperty("status").GetInt32(), refused.GetProperty("replacesInvoiceId").ValueKind));
            sides.Add(Math.Sign(refused.GetProperty("id").GetGuid().CompareTo(invoiceId)));
            var original = await _api.QueryAsync(mine.TenantId, db => db.OrderInvoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId));
            Assert.Equal((OrderInvoiceStatus.Stamped, (Guid?)null), (original.Status, original.ReplacedByInvoiceId));

            // It is the order's invoice again, and it is replaced.
            var replacement = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
                $"/api/v1/invoicing/invoices/{invoiceId}/substitute", new { receiver = corrected, saveToCustomer = false }));
            Assert.Equal(((int)OrderInvoiceStatus.Stamped, invoiceId), (
                replacement.GetProperty("status").GetInt32(), replacement.GetProperty("replacesInvoiceId").GetGuid()));
        }
        Assert.Equal(2, sides.Count);
    }

    [SqlServerFact]
    public async Task Substitution_ThatTheDatabaseRefuses_ShouldFail_NotBeTakenForSomeoneElseInvoicingTheOrder()
    {
        var mine = await _api.SignUpAsync("sustituir-falla", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        var (order, _) = await PaidOrderAsync(mine.Client, await ProductAsync(mine.Client, "Sustituir falla"), phone: null, name: "Mostrador");
        var invoiceId = (await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = false }))).GetProperty("id").GetGuid();
        var corrected = new { rfc = "XIQB891116QE4", name = "Karla Fuente Nolasco", postalCode = "26015", fiscalRegime = "612", cfdiUse = "S01" };

        // A rule of the database the domain does not know refuses the replacement, as a deadlock or a timeout would fail its
        // save. A substitution holds the invoicing lock, so no one else invoiced the order meanwhile: the error is thrown on
        // (for the execution strategy to retry what it can) and nothing is kept, folio included.
        const string refusedEmail = "rechazada-por-la-base@example.com";
        await _api.QueryAsync(mine.TenantId, db => db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE [OrderInvoices] WITH NOCHECK ADD CONSTRAINT [CK_Test_RefusedByTheDatabase] "
            + $"CHECK ([ReceiverEmail] IS NULL OR [ReceiverEmail] <> N'{refusedEmail}')"));
        HttpResponseMessage refused;
        try
        {
            refused = await mine.Client.PostAsJsonAsync($"/api/v1/invoicing/invoices/{invoiceId}/substitute",
                new { receiver = corrected, saveToCustomer = false, receiverEmail = refusedEmail });
        }
        finally
        {
            await _api.QueryAsync(mine.TenantId, db => db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE [OrderInvoices] DROP CONSTRAINT [CK_Test_RefusedByTheDatabase]"));
        }
        Assert.Equal(HttpStatusCode.InternalServerError, refused.StatusCode);
        Assert.DoesNotContain("Otra persona está facturando", await refused.Content.ReadAsStringAsync());
        var original = await _api.QueryAsync(mine.TenantId, db => db.OrderInvoices.AsNoTracking().SingleAsync());
        Assert.Equal((invoiceId, OrderInvoiceStatus.Stamped, (Guid?)null), (original.Id, original.Status, original.ReplacedByInvoiceId));

        // Replaced again, the new one takes the folio that was not kept.
        var replacement = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/invoices/{invoiceId}/substitute", new { receiver = corrected, saveToCustomer = false }));
        Assert.Equal(("F-2", (int)OrderInvoiceStatus.Stamped, invoiceId), (
            replacement.GetProperty("number").GetString(), replacement.GetProperty("status").GetInt32(),
            replacement.GetProperty("replacesInvoiceId").GetGuid()));
    }

    [SqlServerFact]
    public async Task CfdiEmails_OfAnotherBusiness_ShouldBeInvisible_AndUnsendable()
    {
        var mine = await _api.SignUpAsync("correo-cfdi-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("correo-cfdi-b", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        await ReadyToInvoiceAsync(theirs.TenantId, "IIA040805DZ4");
        var (order, _) = await PaidOrderAsync(mine.Client, await ProductAsync(mine.Client, "Correo A"), phone: "5512345678", name: "Karla");
        var karla = $"karla.{Guid.NewGuid():N}@ejemplo.mx";

        // Stamped with an email, its XML and PDF go out from the outbox.
        var invoice = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = false, receiverEmail = karla }));
        var invoiceId = invoice.GetProperty("id").GetGuid();
        Assert.Equal(karla, invoice.GetProperty("receiverEmail").GetString());
        await _api.DispatchOutboxAsync();
        var sent = Assert.Single(_api.DocumentsTo(karla));
        Assert.Equal([".xml", ".pdf"], sent.Attachments.Select(a => Path.GetExtension(a.FileName)));

        // The other owner, even with the header of my business, cannot send it anywhere.
        var other = $"otro.{Guid.NewGuid():N}@ejemplo.mx";
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/invoices/{invoiceId}/email", new { email = other, useForInvoice = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(
            mine, HttpMethod.Post, $"/api/v1/invoicing/invoices/{invoiceId}/email", new { email = other, useForInvoice = true }))).StatusCode);
        await _api.DispatchOutboxAsync();
        Assert.Empty(_api.DocumentsTo(other));
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.CfdiEmails.ToListAsync()));
        Assert.Equal(karla, await _api.QueryAsync(mine.TenantId, db => db.OrderInvoices.Select(i => i.ReceiverEmail).SingleAsync()));

        // Whoever manages the orders sends it again, to the address they type.
        var accounting = $"contabilidad.{Guid.NewGuid():N}@ejemplo.mx";
        var again = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/invoices/{invoiceId}/email", new { email = accounting, useForInvoice = false }));
        Assert.Equal((accounting, false), (again.GetProperty("to").GetString(), again.GetProperty("byTienditApp").GetBoolean()));
        Assert.Equal(HttpStatusCode.BadRequest, (await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/invoices/{invoiceId}/email", new { email = "contabilidad", useForInvoice = false })).StatusCode);
        await _api.DispatchOutboxAsync();
        Assert.Single(_api.DocumentsTo(accounting));
        var emails = (await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/invoicing/orders/{order}")))
            .GetProperty("invoices")[0].GetProperty("emails");
        Assert.Equal(2, emails.GetArrayLength());
        Assert.All(emails.EnumerateArray(), e => Assert.True(e.GetProperty("sentAt").ValueKind == JsonValueKind.String));

        // With 4 today, two people send it at once: the invoicing lock of the business counts them one after the other.
        for (var i = 0; i < 2; i++)
            await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
                $"/api/v1/invoicing/invoices/{invoiceId}/email", new { email = accounting, useForInvoice = false }));
        var answers = await _api.QueryAsync(mine.TenantId, async db =>
        {
            await using var held = await db.Database.BeginTransactionAsync();
            var resource = $"invoicing:{mine.TenantId:N}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"EXEC sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction'");
            var both = Enumerable.Range(0, 2)
                .Select(_ => mine.Client.PostAsJsonAsync($"/api/v1/invoicing/invoices/{invoiceId}/email", new { email = accounting, useForInvoice = false }))
                .ToList();
            await Task.Delay(TimeSpan.FromSeconds(2));
            await held.CommitAsync();
            return await Task.WhenAll(both);
        });
        Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.OK);
        var refused = Assert.Single(answers, answer => answer.StatusCode != HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(CfdiEmails.TooManyForCfdiMessage(invoice.GetProperty("number").GetString()!),
            JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString());
        Assert.Equal(CfdiEmails.MaxPerCfdiPerDay,
            await _api.QueryAsync(mine.TenantId, db => db.CfdiEmails.CountAsync(e => e.CfdiId == invoiceId)));

        // A credit note of the invoice goes by its own route, which the other business does not reach either.
        await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync($"/api/v1/orders/{order}/refunds",
            new { amount = 5m, method = 1, reason = "Faltó uno", restockItems = false }));
        var refund = (await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/invoicing/orders/{order}")))
            .GetProperty("adjustments")[0].GetProperty("adjustmentId").GetGuid();
        var noteId = (await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsync($"/api/v1/invoicing/refunds/{refund}/credit-notes", null)))
            .GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/credit-notes/{noteId}/email", new { email = other, useForInvoice = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(
            mine, HttpMethod.Post, $"/api/v1/invoicing/credit-notes/{noteId}/email", new { email = other, useForInvoice = false }))).StatusCode);
        var noteEmail = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/credit-notes/{noteId}/email", new { email = accounting, useForInvoice = false }));
        Assert.Equal(((int)CfdiEmailDocument.CreditNote, noteId, accounting), (
            noteEmail.GetProperty("document").GetInt32(), noteEmail.GetProperty("cfdiId").GetGuid(), noteEmail.GetProperty("to").GetString()));
        await _api.DispatchOutboxAsync();
        Assert.Empty(_api.DocumentsTo(other));
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.CfdiEmails.ToListAsync()));
        Assert.Contains(_api.DocumentsTo(accounting), d => d.Subject.StartsWith("Nota de crédito NC-1", StringComparison.Ordinal));
    }

    [SqlServerFact]
    public async Task CancellationNotice_ShouldReachOnlyMyCustomer_WhenTheSatCancelsMyInvoice()
    {
        var mine = await _api.SignUpAsync("aviso-cfdi-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("aviso-cfdi-b", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        await ReadyToInvoiceAsync(theirs.TenantId, "IIA040805DZ4");
        var (order, _) = await PaidOrderAsync(mine.Client, await ProductAsync(mine.Client, "Aviso A"), phone: "5512345678", name: "Karla");
        var karla = $"karla.{Guid.NewGuid():N}@ejemplo.mx";
        var invoice = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = false, receiverEmail = karla }));
        var invoiceId = invoice.GetProperty("id").GetGuid();
        _api.Invoicing.CancelledAtOnce[invoice.GetProperty("uuid").GetString()!] = true;
        await _api.DispatchOutboxAsync();

        // The other owner, even with the header of my business, cannot cancel it: nothing reaches my customer.
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(
            mine, HttpMethod.Post, $"/api/v1/invoicing/invoices/{invoiceId}/cancel", new { motive = (int)CfdiCancellationMotive.ErrorsWithoutReplacement }))).StatusCode);
        await _api.DispatchOutboxAsync();
        Assert.Single(_api.DocumentsTo(karla));

        // The SAT cancels it at once: the notice goes from the outbox, without files, and Pedidos shows it.
        var cancelled = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/invoices/{invoiceId}/cancel", new { motive = (int)CfdiCancellationMotive.ErrorsWithoutReplacement }));
        Assert.Equal((int)OrderInvoiceStatus.Cancelled, cancelled.GetProperty("status").GetInt32());
        await _api.DispatchOutboxAsync();
        var notice = _api.DocumentsTo(karla)[^1];
        Assert.StartsWith("Cancelación de la factura F-1", notice.Subject, StringComparison.Ordinal);
        Assert.Empty(notice.Attachments);
        var emails = (await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/invoicing/orders/{order}")))
            .GetProperty("invoices")[0].GetProperty("emails");
        Assert.Equal(((int)CfdiEmailKind.Cancellation, true), (emails[0].GetProperty("kind").GetInt32(), emails[0].GetProperty("byTienditApp").GetBoolean()));
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.CfdiEmails.ToListAsync()));
        Assert.Equal(1, await _api.QueryAsync(mine.TenantId, db => db.CfdiEmails.CountAsync(e => e.Kind == CfdiEmailKind.Cancellation)));
    }

    [SqlServerFact]
    public async Task PaymentComplements_OfAnotherBusiness_ShouldBeInvisible_AndUntouchable()
    {
        var mine = await _api.SignUpAsync("complementos-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("complementos-b", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        await ReadyToInvoiceAsync(theirs.TenantId, "IIA040805DZ4");
        var product = await ProductAsync(mine.Client, "Complemento A");
        await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/cash/open", new { openingAmount = 0m });
        var customer = await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/customers", new { name = "Karla", phone = "5512345678" });
        await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync($"/api/v1/customers/{customer}/credit-limit", new { creditLimit = 5_000m }));
        // Sold on credit with a down payment: invoiced in installments, and the down payment gets its complement.
        var sale = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync("/api/v1/orders", new
        {
            orderType = 0,
            channel = 0,
            createdByBusiness = true,
            customerId = customer,
            onCredit = true,
            items = new[] { new { productId = product, quantity = 2m } },
            payments = new[] { new { amount = 10m, method = 0 } }
        }));
        var order = sale.GetProperty("id").GetGuid();
        var invoice = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = false }));
        Assert.Equal("PPD", invoice.GetProperty("paymentMethod").GetString());
        var payment = (await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/invoicing/orders/{order}")))
            .GetProperty("payments")[0].GetProperty("orderPaymentId").GetGuid();

        // The other owner, even with the header of my business, cannot issue the complement of my payment.
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsync($"/api/v1/invoicing/payments/{payment}/complements", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(
            mine, HttpMethod.Post, $"/api/v1/invoicing/payments/{payment}/complements"))).StatusCode);
        var complement = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsync($"/api/v1/invoicing/payments/{payment}/complements", null));
        var complementId = complement.GetProperty("id").GetGuid();
        Assert.Equal(((int)OrderInvoiceStatus.Stamped, "P-1", 1), (
            complement.GetProperty("status").GetInt32(), complement.GetProperty("number").GetString(), complement.GetProperty("installment").GetInt32()));

        // Nor see, retry, cancel, download or send it.
        foreach (var url in new[] { $"/api/v1/invoicing/payment-complements/{complementId}/xml", $"/api/v1/invoicing/payment-complements/{complementId}/pdf" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(mine, HttpMethod.Get, url))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsync($"/api/v1/invoicing/payment-complements/{complementId}/retry", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(
            mine, HttpMethod.Post, $"/api/v1/invoicing/payment-complements/{complementId}/cancel", new { motive = (int)CfdiCancellationMotive.NotCarriedOut }))).StatusCode);
        var other = $"otro.{Guid.NewGuid():N}@ejemplo.mx";
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/payment-complements/{complementId}/email", new { email = other, useForInvoice = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(
            mine, HttpMethod.Post, $"/api/v1/invoicing/payment-complements/{complementId}/email", new { email = other, useForInvoice = false }))).StatusCode);
        Assert.Empty(_api.Invoicing.Cancellations.Where(c => c.Uuid == complement.GetProperty("uuid").GetString()));
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.PaymentComplements.ToListAsync()));
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.CfdiEmails.ToListAsync()));
        Assert.Empty(await _api.QueryAsync(mine.TenantId, db => db.CfdiEmails.ToListAsync()));

        // Mine: its XML downloads, it goes by email as a complement, and the invoice is not cancelled while it stands.
        Assert.Equal(HttpStatusCode.OK, (await mine.Client.GetAsync($"/api/v1/invoicing/payment-complements/{complementId}/xml")).StatusCode);
        var accounting = $"contabilidad.{Guid.NewGuid():N}@ejemplo.mx";
        var sent = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/payment-complements/{complementId}/email", new { email = accounting, useForInvoice = false }));
        Assert.Equal(((int)CfdiEmailDocument.PaymentComplement, complementId, accounting), (
            sent.GetProperty("document").GetInt32(), sent.GetProperty("cfdiId").GetGuid(), sent.GetProperty("to").GetString()));
        await _api.DispatchOutboxAsync();
        Assert.Empty(_api.DocumentsTo(other));
        Assert.StartsWith("Complemento de pago P-1", Assert.Single(_api.DocumentsTo(accounting)).Subject, StringComparison.Ordinal);
        var blocked = await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/invoices/{invoice.GetProperty("id").GetGuid()}/cancel", new { motive = (int)CfdiCancellationMotive.ErrorsWithoutReplacement });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Contains("P-1", await blocked.Content.ReadAsStringAsync());
    }

    [SqlServerFact]
    public async Task CreditNotes_OfAnotherBusiness_ShouldBeInvisible_AndUntouchable()
    {
        var mine = await _api.SignUpAsync("notas-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("notas-b", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        await ReadyToInvoiceAsync(theirs.TenantId, "IIA040805DZ4");
        var (order, _) = await PaidOrderAsync(mine.Client, await ProductAsync(mine.Client, "Nota A"), phone: "5512345678", name: "Karla");
        var invoice = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = false }));
        // Part of it is given back by card: the refund gets its credit note.
        await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync($"/api/v1/orders/{order}/refunds",
            new { amount = 5m, method = 1, reason = "Faltó uno", restockItems = false }));
        var refund = (await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/invoicing/orders/{order}")))
            .GetProperty("adjustments")[0].GetProperty("adjustmentId").GetGuid();

        // The other owner, even with the header of my business, cannot issue the credit note of my refund.
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsync($"/api/v1/invoicing/refunds/{refund}/credit-notes", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(
            mine, HttpMethod.Post, $"/api/v1/invoicing/refunds/{refund}/credit-notes"))).StatusCode);
        var note = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsync($"/api/v1/invoicing/refunds/{refund}/credit-notes", null));
        var noteId = note.GetProperty("id").GetGuid();
        Assert.Equal(((int)OrderInvoiceStatus.Stamped, "NC-1", 5m), (
            note.GetProperty("status").GetInt32(), note.GetProperty("number").GetString(), note.GetProperty("total").GetDecimal()));

        // Nor see, retry, cancel or download it.
        foreach (var url in new[] { $"/api/v1/invoicing/credit-notes/{noteId}/xml", $"/api/v1/invoicing/credit-notes/{noteId}/pdf" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(mine, HttpMethod.Get, url))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsync($"/api/v1/invoicing/credit-notes/{noteId}/retry", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(
            mine, HttpMethod.Post, $"/api/v1/invoicing/credit-notes/{noteId}/cancel", new { motive = (int)CfdiCancellationMotive.NotCarriedOut }))).StatusCode);
        Assert.DoesNotContain(_api.Invoicing.Cancellations, c => c.Uuid == note.GetProperty("uuid").GetString());
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.CreditNotes.ToListAsync()));

        // Mine: its XML downloads, and the invoice is not cancelled while the credit note stands.
        Assert.Equal(HttpStatusCode.OK, (await mine.Client.GetAsync($"/api/v1/invoicing/credit-notes/{noteId}/xml")).StatusCode);
        var blocked = await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/invoices/{invoice.GetProperty("id").GetGuid()}/cancel", new { motive = (int)CfdiCancellationMotive.ErrorsWithoutReplacement });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Contains("NC-1", await blocked.Content.ReadAsStringAsync());
    }

    [SqlServerFact]
    public async Task CreditNotes_OfAnotherBusinesssWriteOff_ShouldBeInvisible_AndUntouchable()
    {
        var mine = await _api.SignUpAsync("condonacion-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("condonacion-b", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        await ReadyToInvoiceAsync(theirs.TenantId, "IIA040805DZ4");
        var product = await ProductAsync(mine.Client, "Condonación A");
        await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/cash/open", new { openingAmount = 0m });
        var customer = await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/customers", new { name = "Karla", phone = "5512345678" });
        await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync($"/api/v1/customers/{customer}/credit-limit", new { creditLimit = 5_000m }));
        var sale = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync("/api/v1/orders", new
        {
            orderType = 0,
            channel = 0,
            createdByBusiness = true,
            customerId = customer,
            onCredit = true,
            items = new[] { new { productId = product, quantity = 2m } },
            payments = new[] { new { amount = 10m, method = 0 } }
        }));
        var order = sale.GetProperty("id").GetGuid();
        await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = false }));
        // Part of what the customer owes is forgiven: the write-off gets its credit note.
        await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync("/api/v1/customers/account/write-offs",
            new { customerId = customer, orderId = order, amount = 5m, reason = 3, expectedOwed = sale.GetProperty("balance").GetDecimal() }));
        var writeOff = (await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/invoicing/orders/{order}")))
            .GetProperty("adjustments")[0].GetProperty("adjustmentId").GetGuid();

        // The other owner, even with the header of my business, cannot issue the credit note of my write-off.
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsync($"/api/v1/invoicing/write-offs/{writeOff}/credit-notes", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(
            mine, HttpMethod.Post, $"/api/v1/invoicing/write-offs/{writeOff}/credit-notes"))).StatusCode);
        var note = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsync($"/api/v1/invoicing/write-offs/{writeOff}/credit-notes", null));
        var noteId = note.GetProperty("id").GetGuid();
        Assert.Equal(("NC-1", "15", 5m), (
            note.GetProperty("number").GetString(), note.GetProperty("paymentForm").GetString(), note.GetProperty("total").GetDecimal()));

        // Nor ask the PAC again for it with my header, nor cancel it with theirs.
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(
            mine, HttpMethod.Post, $"/api/v1/invoicing/credit-notes/{noteId}/retry"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/credit-notes/{noteId}/cancel", new { motive = (int)CfdiCancellationMotive.NotCarriedOut })).StatusCode);
        Assert.DoesNotContain(_api.Invoicing.Cancellations, c => c.Uuid == note.GetProperty("uuid").GetString());
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.CreditNotes.ToListAsync()));
    }

    [SqlServerFact]
    public async Task TheSameRefundsCreditNoteIssuedAtOnce_ShouldBeStampedOnce_AndGiveTheOtherFolioBack()
    {
        var mine = await _api.SignUpAsync("notas-carrera", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        var (order, _) = await PaidOrderAsync(mine.Client, await ProductAsync(mine.Client, "Nota carrera"), phone: "5512345678", name: "Karla");
        await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = false }));
        foreach (var _ in Enumerable.Range(0, 3))
            await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync($"/api/v1/orders/{order}/refunds",
                new { amount = 1m, method = 1, reason = "Faltó uno", restockItems = false }));
        var refunds = (await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/invoicing/orders/{order}")))
            .GetProperty("adjustments").EnumerateArray().Select(a => a.GetProperty("adjustmentId").GetGuid()).ToList();
        Assert.Equal(3, refunds.Count);
        Assert.Equal("NC-1", (await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsync(
            $"/api/v1/invoicing/refunds/{refunds[0]}/credit-notes", null))).GetProperty("number").GetString());

        // Two people issue the credit note of the same refund at once. While the serie's folio is held, the first waits for a
        // folio; the second waits for the invoicing lock of the business and, once the first commits, finds its credit note.
        // The index of one live credit note per refund stays as the last word.
        var answers = await _api.QueryAsync(mine.TenantId, async db =>
        {
            await using var held = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE FolioSequences SET LastNumber = LastNumber WHERE TenantId = {mine.TenantId}");
            var both = Enumerable.Range(0, 2)
                .Select(_ => mine.Client.PostAsync($"/api/v1/invoicing/refunds/{refunds[1]}/credit-notes", null))
                .ToList();
            await Task.Delay(TimeSpan.FromSeconds(2));
            await held.CommitAsync();
            return await Task.WhenAll(both);
        });

        Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.OK);
        var refused = Assert.Single(answers, answer => answer.StatusCode != HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains(
            JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString(),
            new[]
            {
                "La nota de crédito NC-2 de este reembolso se está timbrando.",
                "Este reembolso ya tiene la nota de crédito NC-2: cancélala para emitir otra.",
                CreditNotes.AlreadyIssuingMessage
            });
        Assert.Equal(2, await _api.QueryAsync(mine.TenantId, db => db.CreditNotes.CountAsync()));
        // The refused one took no folio: the next credit note of the serie follows without a hole.
        Assert.Equal("NC-3", (await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsync(
            $"/api/v1/invoicing/refunds/{refunds[2]}/credit-notes", null))).GetProperty("number").GetString());
    }

    [SqlServerFact]
    public async Task TheSamePaymentsComplementIssuedAtOnce_ShouldBeStampedOnce_AndGiveTheOtherFolioBack()
    {
        var mine = await _api.SignUpAsync("complementos-carrera", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        var product = await ProductAsync(mine.Client, "Complemento carrera");
        await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/cash/open", new { openingAmount = 0m });
        var customer = await TwoBusinessesApi.IdAsync(mine.Client, "/api/v1/customers", new { name = "Karla", phone = "5512345678" });
        await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync($"/api/v1/customers/{customer}/credit-limit", new { creditLimit = 5_000m }));
        var order = (await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync("/api/v1/orders", new
        {
            orderType = 0,
            channel = 0,
            createdByBusiness = true,
            customerId = customer,
            onCredit = true,
            items = new[] { new { productId = product, quantity = 3m } },
            payments = new[] { new { amount = 10m, method = 0 } }
        }))).GetProperty("id").GetGuid();
        foreach (var _ in Enumerable.Range(0, 2))
            await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync($"/api/v1/orders/{order}/payments", new { amount = 10m, method = 0 }));
        var total = (await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = false }))).GetProperty("total").GetDecimal();
        var payments = (await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/invoicing/orders/{order}")))
            .GetProperty("payments").EnumerateArray().Select(p => p.GetProperty("orderPaymentId").GetGuid()).ToList();
        Assert.Equal(3, payments.Count);
        Assert.Equal("P-1", (await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsync(
            $"/api/v1/invoicing/payments/{payments[0]}/complements", null))).GetProperty("number").GetString());

        // Two people issue the complement of the same payment at once. While the serie's folio is held, the first waits for
        // a folio; the second waits for the invoicing lock of the business and, once the first commits, finds its complement.
        // The index of one live complement per payment stays as the last word.
        var answers = await _api.QueryAsync(mine.TenantId, async db =>
        {
            await using var held = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE FolioSequences SET LastNumber = LastNumber WHERE TenantId = {mine.TenantId}");
            var both = Enumerable.Range(0, 2)
                .Select(_ => mine.Client.PostAsync($"/api/v1/invoicing/payments/{payments[1]}/complements", null))
                .ToList();
            await Task.Delay(TimeSpan.FromSeconds(2));
            await held.CommitAsync();
            return await Task.WhenAll(both);
        });

        Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.OK);
        var refused = Assert.Single(answers, answer => answer.StatusCode != HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains(
            JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString(),
            new[]
            {
                "El complemento P-2 de este cobro se está timbrando.",
                "Este cobro ya tiene el complemento P-2: cancélalo para emitir otro."
            });
        var complements = await _api.QueryAsync(mine.TenantId, db => db.PaymentComplements.OrderBy(c => c.Installment).ToListAsync());
        Assert.Equal([(1, total, 10m), (2, total - 10m, 10m)], complements.Select(c => (c.Installment, c.PreviousBalance, c.Amount)));
        // The refused one took no folio: the next complement of the serie follows without a hole.
        Assert.Equal("P-3", (await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsync(
            $"/api/v1/invoicing/payments/{payments[2]}/complements", null))).GetProperty("number").GetString());
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
        // Keeping the fiscal data in the customer changes the customer: whoever does not manage customers stamps without it.
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = true })).StatusCode);
        var invoice = await TwoBusinessesApi.ReadAsync(await cashier.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = false }));
        var invoiceId = invoice.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync(
            $"/api/v1/invoicing/invoices/{invoiceId}/cancel", new { motive = (int)CfdiCancellationMotive.NotCarriedOut })).StatusCode);
        // Replacing it cancels it too (Fase 28G): only owners.
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync(
            $"/api/v1/invoicing/invoices/{invoiceId}/substitute", new { receiver = Receiver, saveToCustomer = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsync(
            $"/api/v1/invoicing/invoices/{invoiceId}/cancel-replaced", null)).StatusCode);
        // Sending it by email is managing the orders (Fase 28E).
        Assert.Equal(HttpStatusCode.Forbidden, (await stock.PostAsJsonAsync(
            $"/api/v1/invoicing/invoices/{invoiceId}/email", new { email = "karla@ejemplo.mx", useForInvoice = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.PostAsJsonAsync(
            $"/api/v1/invoicing/invoices/{invoiceId}/email", new { email = "karla@ejemplo.mx", useForInvoice = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync($"/api/v1/invoicing/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.DeleteAsync($"/api/v1/invoicing/settings/certificate")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PutAsJsonAsync($"/api/v1/invoicing/settings/serie", new { serie = "X" })).StatusCode);

        var cancelling = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/invoices/{invoiceId}/cancel", new { motive = (int)CfdiCancellationMotive.NotCarriedOut }));
        Assert.Equal((int)OrderInvoiceStatus.Cancelling, cancelling.GetProperty("status").GetInt32());
        Assert.Equal(2, await _api.QueryAsync(mine.TenantId, db => db.AuditLogs.CountAsync(a => a.EntityName == nameof(OrderInvoice))));
    }

    [SqlServerFact]
    public async Task IssuedCfdis_OfAnotherBusiness_ShouldBeOutOfItsList_SummaryAndDownload_AndOnlyOwnersSeeThem()
    {
        var mine = await _api.SignUpAsync("emitidos-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("emitidos-b", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        await ReadyToInvoiceAsync(theirs.TenantId, "IIA040805DZ4");
        var (order, total) = await PaidOrderAsync(mine.Client, await ProductAsync(mine.Client, "Emitida A"), phone: "5512345678", name: "Karla");
        var invoice = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = false }));
        var uuid = invoice.GetProperty("uuid").GetString()!;
        // The month the invoice is dated in, in the business's time zone (Mexico City).
        var dated = TimeZoneInfo.ConvertTime(invoice.GetProperty("requestedAt").GetDateTimeOffset(), TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City"));
        var month = $"year={dated.Year}&month={dated.Month}";

        var listed = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync($"/api/v1/invoicing/issued-cfdis?{month}"));
        var item = Assert.Single(listed.GetProperty("items").EnumerateArray());
        Assert.Equal(("F-1", uuid, "XIQB891116QE4", total, true), (item.GetProperty("number").GetString(), item.GetProperty("uuid").GetString(),
            item.GetProperty("receiverRfc").GetString(), item.GetProperty("total").GetDecimal(), item.GetProperty("hasXml").GetBoolean()));
        var zip = await mine.Client.GetAsync($"/api/v1/invoicing/issued-cfdis/export?{month}");
        Assert.Equal(("application/zip", true), (zip.Content.Headers.ContentType?.MediaType, zip.Headers.CacheControl is { Private: true, NoStore: true }));
        using (var archive = new System.IO.Compression.ZipArchive(await zip.Content.ReadAsStreamAsync()))
            Assert.Contains($"ingresos/F-1_{uuid}.xml", archive.Entries.Select(e => e.FullName));
        var list = await mine.Client.GetAsync($"/api/v1/invoicing/issued-cfdis/export?{month}&format=Csv");
        Assert.Equal(("text/csv", true), (list.Content.Headers.ContentType?.MediaType, list.Headers.CacheControl is { Private: true, NoStore: true }));
        Assert.Contains($",{uuid},", await list.Content.ReadAsStringAsync());

        // The other owner, even with the header of my business, has none: an empty list, a summary of zeros, a ZIP with
        // only its empty list.
        foreach (var send in new Func<string, Task<HttpResponseMessage>>[]
        {
            url => theirs.Client.GetAsync(url),
            url => theirs.Client.SendAsync(AsOther(mine, HttpMethod.Get, url))
        })
        {
            Assert.Equal(0, (await TwoBusinessesApi.ReadAsync(await send($"/api/v1/invoicing/issued-cfdis?{month}"))).GetProperty("totalCount").GetInt32());
            var summary = await TwoBusinessesApi.ReadAsync(await send($"/api/v1/invoicing/issued-cfdis/summary?{month}"));
            Assert.All(summary.GetProperty("kinds").EnumerateArray(), k => Assert.Equal(0, k.GetProperty("inForce").GetInt32()));
            var theirZip = await send($"/api/v1/invoicing/issued-cfdis/export?{month}");
            Assert.Equal(HttpStatusCode.OK, theirZip.StatusCode);
            using var archive = new System.IO.Compression.ZipArchive(await theirZip.Content.ReadAsStreamAsync());
            Assert.Equal([$"CFDI_{dated.Year:0000}-{dated.Month:00}.csv"], archive.Entries.Select(e => e.FullName));
            using var csv = new StreamReader(archive.Entries[0].Open());
            Assert.DoesNotContain(uuid, await csv.ReadToEndAsync());
            Assert.DoesNotContain(uuid, await (await send($"/api/v1/invoicing/issued-cfdis/export?{month}&format=Csv")).Content.ReadAsStringAsync());
        }

        // The month's fiscal list carries the customers' RFC and name: owners and admins only, not who manages the orders.
        var cashier = await EmployeeAsync(mine, "ManageOrders", "ChargeOrders", "ManageCash", "ViewReports");
        foreach (var url in new[]
        {
            $"/api/v1/invoicing/issued-cfdis?{month}",
            $"/api/v1/invoicing/issued-cfdis/summary?{month}",
            $"/api/v1/invoicing/issued-cfdis/export?{month}"
        })
            Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(url)).StatusCode);
    }

    [SqlServerFact]
    public async Task AccountantEmails_OfAnotherBusiness_ShouldBeOutOfItsList_ItsMonthUnsendable_AndOnlyOwnersSendThem()
    {
        var mine = await _api.SignUpAsync("contador-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("contador-b", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        await ReadyToInvoiceAsync(theirs.TenantId, "IIA040805DZ4");
        var (order, _) = await PaidOrderAsync(mine.Client, await ProductAsync(mine.Client, "Contador A"), phone: "5512345678", name: "Karla");
        var invoice = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = false }));
        var dated = TimeZoneInfo.ConvertTime(invoice.GetProperty("requestedAt").GetDateTimeOffset(), TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City"));
        var month = new { year = dated.Year, month = dated.Month, email = "contador@despacho.mx" };
        var emails = $"/api/v1/invoicing/issued-cfdis/accountant-emails?year={dated.Year}&month={dated.Month}";

        var set = await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync(
            "/api/v1/invoicing/settings/accountant", new { email = "contador@despacho.mx", monthly = true }));
        Assert.Equal(("contador@despacho.mx", true), (set.GetProperty("accountantEmail").GetString(), set.GetProperty("sendsMonthToAccountant").GetBoolean()));
        var sent = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync("/api/v1/invoicing/issued-cfdis/accountant-emails", month));
        Assert.Equal(("contador@despacho.mx", false), (sent.GetProperty("to").GetString(), sent.GetProperty("byTienditApp").GetBoolean()));
        Assert.Single((await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync(emails))).EnumerateArray());

        // The other owner, even with the header of my business, sees none of my emails, has no CFDI of mine to send, and
        // changes only its own accountant.
        foreach (var send in new Func<HttpMethod, string, object?, Task<HttpResponseMessage>>[]
        {
            (method, url, body) => theirs.Client.SendAsync(new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) }),
            (method, url, body) => theirs.Client.SendAsync(AsOther(mine, method, url, body))
        })
        {
            Assert.Empty((await TwoBusinessesApi.ReadAsync(await send(HttpMethod.Get, emails, null))).EnumerateArray());
            var refused = await send(HttpMethod.Post, "/api/v1/invoicing/issued-cfdis/accountant-emails", month);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains(AccountantCfdiEmails.NoCfdiMessage, await refused.Content.ReadAsStringAsync());
            var theirSettings = await TwoBusinessesApi.ReadAsync(await send(
                HttpMethod.Put, "/api/v1/invoicing/settings/accountant", new { email = "otro@despacho.mx", monthly = false }));
            Assert.Equal("otro@despacho.mx", theirSettings.GetProperty("accountantEmail").GetString());
        }
        var mineNow = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync("/api/v1/invoicing/settings"));
        Assert.Equal(("contador@despacho.mx", true), (mineNow.GetProperty("accountantEmail").GetString(), mineNow.GetProperty("sendsMonthToAccountant").GetBoolean()));
        Assert.Equal(1, await _api.QueryAsync(mine.TenantId, db => db.CfdiMonthEmails.CountAsync()));

        // The month's fiscal file goes out of the business: owners and admins only.
        var cashier = await EmployeeAsync(mine, "ManageOrders", "ChargeOrders", "ManageCash", "ViewReports");
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(emails)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync("/api/v1/invoicing/issued-cfdis/accountant-emails", month)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PutAsJsonAsync(
            "/api/v1/invoicing/settings/accountant", new { email = "yo@ejemplo.mx", monthly = true })).StatusCode);
    }

    [SqlServerFact]
    public async Task CfdisToReview_OfAnotherBusiness_ShouldBeOutOfItsList_AndUnresolvable_AndOnlyOwnersResolveThem()
    {
        var mine = await _api.SignUpAsync("revisar-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("revisar-b", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        await ReadyToInvoiceAsync(theirs.TenantId, "IIA040805DZ4");
        var (order, _) = await PaidOrderAsync(mine.Client, await ProductAsync(mine.Client, "Revisar A"), phone: "5512345678", name: "Karla");
        var invoice = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = false }));
        var invoiceId = invoice.GetProperty("id").GetGuid();
        var uuid = invoice.GetProperty("uuid").GetString()!;
        // Its answer was lost three days ago: it stayed «Timbrando» past the PAC's window.
        await _api.QueryAsync(mine.TenantId, db => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE OrderInvoices SET Status = 0, Uuid = NULL, ProviderInvoiceId = NULL, StampedAt = NULL, XmlFileKey = NULL,
                PdfFileKey = NULL, IssuedAt = DATEADD(hour, -80, IssuedAt), RequestedAt = DATEADD(hour, -80, RequestedAt)
            WHERE Id = {invoiceId}
            """));
        var stamped = $"/api/v1/invoicing/issued-cfdis/OrderInvoice/{invoiceId}/stamped";
        var notStamped = $"/api/v1/invoicing/issued-cfdis/OrderInvoice/{invoiceId}/not-stamped";

        // The other owner, even with the header of my business, has nothing to review and cannot resolve mine.
        foreach (var send in new Func<HttpMethod, string, object?, Task<HttpResponseMessage>>[]
        {
            (method, url, body) => theirs.Client.SendAsync(new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) }),
            (method, url, body) => theirs.Client.SendAsync(AsOther(mine, method, url, body))
        })
        {
            Assert.Empty((await TwoBusinessesApi.ReadAsync(await send(HttpMethod.Get, "/api/v1/invoicing/issued-cfdis/to-review", null))).EnumerateArray());
            Assert.Equal(HttpStatusCode.NotFound, (await send(HttpMethod.Post, stamped, new { uuid })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await send(HttpMethod.Post, notStamped, null)).StatusCode);
        }

        // Owners and admins only: it decides whether what the CFDI covered can be invoiced again.
        var cashier = await EmployeeAsync(mine, "ManageOrders", "ChargeOrders", "ManageCash", "ViewReports");
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("/api/v1/invoicing/issued-cfdis/to-review")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsJsonAsync(stamped, new { uuid })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsync(notStamped, null)).StatusCode);

        var toReview = await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync("/api/v1/invoicing/issued-cfdis/to-review"));
        Assert.Equal(invoiceId, Assert.Single(toReview.EnumerateArray()).GetProperty("id").GetGuid());
        var brought = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(stamped, new { uuid = uuid.ToLowerInvariant() }));
        Assert.Equal(((int)OrderInvoiceStatus.Stamped, uuid), (brought.GetProperty("status").GetInt32(), brought.GetProperty("uuid").GetString()));
        Assert.Empty((await TwoBusinessesApi.ReadAsync(await mine.Client.GetAsync("/api/v1/invoicing/issued-cfdis/to-review"))).EnumerateArray());
    }

    [SqlServerFact]
    public async Task TheSameOrderInvoicedAtOnce_ShouldBeStampedOnce_AndGiveTheOtherFolioBack()
    {
        var mine = await _api.SignUpAsync("facturas-carrera", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        var product = await ProductAsync(mine.Client, "Factura carrera");
        var (first, _) = await PaidOrderAsync(mine.Client, product, phone: null, name: "Mostrador");
        var (raced, _) = await PaidOrderAsync(mine.Client, product, phone: null, name: "Mostrador");
        var (next, _) = await PaidOrderAsync(mine.Client, product, phone: null, name: "Mostrador");
        var body = new { receiver = Receiver, saveToCustomer = false };
        Assert.Equal("F-1", (await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{first}/invoices", body))).GetProperty("number").GetString());

        // Two people stamp the same order at once. While the serie's folio is held, the first finds no live invoice and
        // waits for a folio; the second waits for the invoicing lock of the business (Fase 28B) and, once the first
        // commits, finds its invoice. The index of one live invoice per order stays as the last word.
        var answers = await _api.QueryAsync(mine.TenantId, async db =>
        {
            await using var held = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE FolioSequences SET LastNumber = LastNumber WHERE TenantId = {mine.TenantId}");
            var both = Enumerable.Range(0, 2)
                .Select(_ => mine.Client.PostAsJsonAsync($"/api/v1/invoicing/orders/{raced}/invoices", body))
                .ToList();
            await Task.Delay(TimeSpan.FromSeconds(2));
            await held.CommitAsync();
            return await Task.WhenAll(both);
        });

        Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.OK);
        var refused = Assert.Single(answers, answer => answer.StatusCode != HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        // The first one's invoice, timbrando or already stamped: it is stamped after its commit, when the second gets the lock.
        Assert.Contains(
            JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString(),
            new[]
            {
                "La factura F-2 de este pedido se está timbrando.",
                "Este pedido ya tiene la factura F-2: cancélala para facturarlo otra vez."
            });
        Assert.Equal(1, await _api.QueryAsync(mine.TenantId, db => db.OrderInvoices.CountAsync(i => i.OrderId == raced)));
        // The refused one took no folio: the next invoice of the serie follows without a hole.
        Assert.Equal("F-3", (await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{next}/invoices", body))).GetProperty("number").GetString());
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
            $"/api/v1/customers/{customer}/fiscal-data", new { fiscalData = Receiver, invoiceEmail = "facturas@ejemplo.mx" }));
        Assert.Equal("KARLA FUENTE NOLASCO", saved.GetProperty("fiscalData").GetProperty("name").GetString());
        Assert.Equal("facturas@ejemplo.mx", saved.GetProperty("invoiceEmail").GetString());
        // The list in Clientes carries it too: the fiscal data opens from there with it.
        var listed = await mine.Client.GetFromJsonAsync<JsonElement>("/api/v1/customers?search=Karla");
        Assert.Equal("facturas@ejemplo.mx", listed.GetProperty("items").EnumerateArray().Single().GetProperty("invoiceEmail").GetString());
        Assert.Empty((await theirs.Client.GetFromJsonAsync<JsonElement>("/api/v1/customers?search=Karla")).GetProperty("items").EnumerateArray());
    }

    [SqlServerFact]
    public async Task WhatsApp_ShouldSendOnlyMyCfdi_AndItsLinkShouldOpenOnlyThatCfdi()
    {
        var mine = await _api.SignUpAsync("whatsapp-factura-a", TwoBusinessesApi.FoodBusiness);
        var theirs = await _api.SignUpAsync("whatsapp-factura-b", TwoBusinessesApi.FoodBusiness);
        await ReadyToInvoiceAsync(mine.TenantId, "EKU9003173C9");
        await ReadyToInvoiceAsync(theirs.TenantId, "IIA040805DZ4");
        // My WhatsApp is connected; theirs is not. The messaging worker is off in the suite: nothing reaches Meta.
        await TwoBusinessesApi.ReadAsync(await mine.Client.PutAsJsonAsync("/api/v1/auth/my-tenant/whatsapp", new
        {
            isEnabled = true,
            accessToken = "token-de-prueba",
            phoneNumberId = $"9{Random.Shared.NextInt64(10_000_000_000L, 99_999_999_999L)}",
            wabaId = "123456789012345"
        }));
        var (order, _) = await PaidOrderAsync(mine.Client, await ProductAsync(mine.Client, "WhatsApp A"), phone: "5512345678", name: "Karla");
        var (theirOrder, _) = await PaidOrderAsync(theirs.Client, await ProductAsync(theirs.Client, "WhatsApp B"), phone: "5512345678", name: "Karla");

        // Without its WhatsApp connected, a business does not invoice to a WhatsApp.
        var notConnected = await theirs.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{theirOrder}/invoices", new { receiver = Receiver, saveToCustomer = false, receiverWhatsApp = "5512345678" });
        Assert.Equal(HttpStatusCode.BadRequest, notConnected.StatusCode);
        Assert.Contains("Conecta el WhatsApp", await notConnected.Content.ReadAsStringAsync());

        // Mine: the order's phone is proposed, and the stamp queues the PDF to it.
        var invoicing = await mine.Client.GetFromJsonAsync<JsonElement>($"/api/v1/invoicing/orders/{order}");
        Assert.Equal((true, "+525512345678"), (invoicing.GetProperty("whatsAppConnected").GetBoolean(), invoicing.GetProperty("proposedWhatsApp").GetString()));
        var invoice = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/orders/{order}/invoices", new { receiver = Receiver, saveToCustomer = false, receiverWhatsApp = "55 1234 5678" }));
        var invoiceId = invoice.GetProperty("id").GetGuid();
        Assert.Equal("+525512345678", invoice.GetProperty("receiverWhatsApp").GetString());
        var queued = await _api.QueryAsync(mine.TenantId, db => db.OutgoingMessages.SingleAsync(m => m.CfdiId == invoiceId));
        Assert.Equal(("+525512345678", (Guid?)null), (queued.Phone, queued.RequestedByUserId));
        // Campañas › Mensajes does not list it: it carries the link that downloads the CFDI, seen only in the order.
        foreach (var listed in new[] { "/api/v1/messaging/messages", $"/api/v1/messaging/messages?purpose={(int)MessagePurpose.CfdiDocument}" })
            Assert.Empty((await mine.Client.GetFromJsonAsync<JsonElement>(listed)).GetProperty("items").EnumerateArray());

        // The text of my invoices is the owner's, like choosing it: running campaigns is not enough. It carries the link.
        const string templates = "/api/v1/messaging/templates";
        var campaigns = await EmployeeAsync(mine, "ManageCampaigns");
        Assert.Equal(HttpStatusCode.Forbidden, (await campaigns.PostAsJsonAsync(
            templates, new { name = "Factura", purpose = (int)MessagePurpose.CfdiDocument, body = "Hola {nombre}: {enlace}" })).StatusCode);
        var withoutLink = await mine.Client.PostAsJsonAsync(
            templates, new { name = "Factura", purpose = (int)MessagePurpose.CfdiDocument, body = "Hola {nombre}: va tu factura." });
        Assert.Equal(HttpStatusCode.BadRequest, withoutLink.StatusCode);
        Assert.Contains("{enlace}", await withoutLink.Content.ReadAsStringAsync());
        await TwoBusinessesApi.IdAsync(
            mine.Client, templates, new { name = "Factura", purpose = (int)MessagePurpose.CfdiDocument, body = "Hola {nombre}: {enlace}" });

        // The other owner, even with the header of my business, cannot send it.
        var url = $"/api/v1/invoicing/invoices/{invoiceId}/whatsapp";
        var body = new { phone = "5598765432", useForInvoice = true };
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsJsonAsync(url, body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(mine, HttpMethod.Post, url, body))).StatusCode);
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.OutgoingMessages.Where(m => m.CfdiId != null).ToListAsync()));

        // I send it again to another number, which is kept for what comes next of the invoice.
        var sent = await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(url, body));
        Assert.Equal(("+525598765432", false), (sent.GetProperty("phone").GetString(), sent.GetProperty("byTienditApp").GetBoolean()));
        invoicing = await mine.Client.GetFromJsonAsync<JsonElement>($"/api/v1/invoicing/orders/{order}");
        var mineInvoice = invoicing.GetProperty("invoices")[0];
        Assert.Equal("+525598765432", mineInvoice.GetProperty("receiverWhatsApp").GetString());
        Assert.Equal(2, mineInvoice.GetProperty("whatsApps").GetArrayLength());

        // With 4 today, two people send it at once: the invoicing lock of the business counts them one after the other.
        for (var i = 0; i < 2; i++)
            await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(url, body));
        var answers = await _api.QueryAsync(mine.TenantId, async db =>
        {
            await using var held = await db.Database.BeginTransactionAsync();
            var resource = $"invoicing:{mine.TenantId:N}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"EXEC sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction'");
            var both = Enumerable.Range(0, 2).Select(_ => mine.Client.PostAsJsonAsync(url, body)).ToList();
            await Task.Delay(TimeSpan.FromSeconds(2));
            await held.CommitAsync();
            return await Task.WhenAll(both);
        });
        Assert.Single(answers, answer => answer.StatusCode == HttpStatusCode.OK);
        var refused = Assert.Single(answers, answer => answer.StatusCode != HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(CfdiWhatsApps.TooManyForCfdiMessage("F-1"),
            JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString());
        Assert.Equal(CfdiWhatsApps.MaxPerCfdiPerDay,
            await _api.QueryAsync(mine.TenantId, db => db.OutgoingMessages.CountAsync(m => m.CfdiId == invoiceId)));

        // Its link, without an account and whatever header comes along, opens that CFDI and downloads its XML.
        var anonymous = _api.AnonymousClient();
        var page = await TwoBusinessesApi.ReadAsync(await anonymous.PostAsJsonAsync("/api/v1/public/cfdi", new { token = queued.LinkToken }));
        Assert.Equal(("F-1", true, invoice.GetProperty("uuid").GetString()), (
            page.GetProperty("number").GetString(), page.GetProperty("stands").GetBoolean(), page.GetProperty("uuid").GetString()));
        var xml = await anonymous.PostAsJsonAsync("/api/v1/public/cfdi/files", new { token = queued.LinkToken, kind = (int)OrderInvoiceFileKind.Xml });
        Assert.Equal(HttpStatusCode.OK, xml.StatusCode);
        Assert.Contains(invoice.GetProperty("uuid").GetString()!, await xml.Content.ReadAsStringAsync());
        Assert.True(xml.Headers.CacheControl is { Private: true, NoStore: true });
        Assert.Equal(HttpStatusCode.OK, (await theirs.Client.SendAsync(AsOther(theirs, HttpMethod.Post, "/api/v1/public/cfdi",
            new { token = queued.LinkToken }))).StatusCode);
        // Another link, one changed by a letter, or one of another shape finds nothing, the same for all.
        foreach (var token in new[] { new string('A', 43), queued.LinkToken![..42] + (queued.LinkToken[42] == 'A' ? 'B' : 'A'), "corto" })
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsJsonAsync("/api/v1/public/cfdi", new { token })).StatusCode);

        // Once its cancellation is asked for, the link says so and no longer downloads it.
        await TwoBusinessesApi.ReadAsync(await mine.Client.PostAsJsonAsync(
            $"/api/v1/invoicing/invoices/{invoiceId}/cancel", new { motive = (int)CfdiCancellationMotive.NotCarriedOut }));
        page = await TwoBusinessesApi.ReadAsync(await anonymous.PostAsJsonAsync("/api/v1/public/cfdi", new { token = queued.LinkToken }));
        Assert.False(page.GetProperty("stands").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.PostAsJsonAsync(
            "/api/v1/public/cfdi/files", new { token = queued.LinkToken, kind = (int)OrderInvoiceFileKind.Pdf })).StatusCode);

        // One that WhatsApp did not deliver is seen in the order: Inicio does not count it with the campaigns' that failed.
        await _api.QueryAsync(mine.TenantId, async db =>
        {
            (await db.OutgoingMessages.SingleAsync(m => m.Id == queued.Id)).Fail("WhatsApp rechazó el mensaje.");
            return await db.SaveChangesAsync();
        });
        var attention = await mine.Client.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/attention");
        Assert.DoesNotContain(attention.GetProperty("items").EnumerateArray(), item => item.GetProperty("key").GetString() == "failedMessages");
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
