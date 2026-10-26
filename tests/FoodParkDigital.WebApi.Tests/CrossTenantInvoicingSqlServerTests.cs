using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FoodParkDigital.Application.Invoicing;
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

        // Nor see, retry, cancel or download it.
        foreach (var url in new[] { $"/api/v1/invoicing/payment-complements/{complementId}/xml", $"/api/v1/invoicing/payment-complements/{complementId}/pdf" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(mine, HttpMethod.Get, url))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.PostAsync($"/api/v1/invoicing/payment-complements/{complementId}/retry", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await theirs.Client.SendAsync(AsOther(
            mine, HttpMethod.Post, $"/api/v1/invoicing/payment-complements/{complementId}/cancel", new { motive = (int)CfdiCancellationMotive.NotCarriedOut }))).StatusCode);
        Assert.Empty(_api.Invoicing.Cancellations.Where(c => c.Uuid == complement.GetProperty("uuid").GetString()));
        Assert.Empty(await _api.QueryAsync(theirs.TenantId, db => db.PaymentComplements.ToListAsync()));

        // Mine: its XML downloads, and the invoice is not cancelled while the complement stands.
        Assert.Equal(HttpStatusCode.OK, (await mine.Client.GetAsync($"/api/v1/invoicing/payment-complements/{complementId}/xml")).StatusCode);
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
