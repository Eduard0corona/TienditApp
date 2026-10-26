using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers.Commands;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Fiscal.Events;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The XML and PDF of each stamped CFDI of an order's invoice reach its customer by email (Fase 28E, ADR 0049): TienditApp
/// asks for it with the stamp when the invoice has an email, for the invoice, its payment complements and its credit notes;
/// a person sends one again, within limits; and the outbox sends it, waits a little for the PDF, and records how it ended.
/// </summary>
public class CfdiEmailFlowTests
{
    private const string Rfc = "EKU9003173C9";
    private const string CustomerPhone = "+525512345678";
    private const string Karla = "karla@ejemplo.mx";

    private static readonly CfdiReceiverInput Receiver = new()
    {
        Rfc = "xiqb891116qe4",
        Name = "Karla Fuente Nolasco",
        PostalCode = "26015",
        FiscalRegime = "612",
        CfdiUse = "G03"
    };

    [Fact]
    public async Task Stamp_ShouldAskForTheInvoicesEmail_AndTheQueueSendItsXmlAndPdf_Once()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidSaleAsync();

        var invoice = (await book.IssueAsync(sale.Id, $" {Karla} ")).Value;

        Assert.Equal((OrderInvoiceStatus.Stamped, Karla), (invoice.Status, invoice.ReceiverEmail));
        var email = Assert.Single(await book.Context.CfdiEmails.ToListAsync());
        Assert.Equal((invoice.Id, CfdiEmailDocument.Invoice, invoice.Id, Karla, (Guid?)null, true),
            (email.OrderInvoiceId, email.Document, email.CfdiId, email.To, email.RequestedByUserId, email.IsPending));

        await book.DeliverAsync(email.Id);

        var sent = Assert.Single(book.Email.Documents);
        var stamped = await book.Context.OrderInvoices.SingleAsync();
        Assert.Equal((Karla, "Factura F-1 de Negocio demo", "Negocio demo vía TienditApp", "demo@demo.com"),
            (sent.To, sent.Subject, sent.SenderName, sent.ReplyTo));
        Assert.Equal(
            [($"F-1_{stamped.Uuid}.xml", "application/xml"), ($"F-1_{stamped.Uuid}.pdf", "application/pdf")],
            sent.Attachments.Select(a => (a.FileName, a.ContentType)));
        Assert.Equal(book.Storage.Files[stamped.XmlFileKey!].Content, sent.Attachments[0].Content);
        // The PDF kept when it was stamped: the PAC is not asked again.
        Assert.Equal(book.Storage.Files[stamped.PdfFileKey!].Content, sent.Attachments[1].Content);
        Assert.Equal(1, book.Gateway.PdfRenders);
        Assert.Contains($"Folio fiscal: {stamped.Uuid}", sent.HtmlBody);
        Assert.Contains("verificacfdi.facturaelectronica.sat.gob.mx", sent.HtmlBody);

        var delivered = await book.Context.CfdiEmails.SingleAsync();
        Assert.Equal(((DateTimeOffset?)book.Clock.UtcNow, false, false), (delivered.SentAt, delivered.SentWithoutPdf, delivered.IsPending));

        // The outbox runs it again: it is not sent twice.
        await book.DeliverAsync(email.Id, attempt: 2);
        Assert.Single(book.Email.Documents);

        var shown = Assert.Single((await book.InvoicingAsync(sale.Id)).Invoices);
        Assert.Equal(Karla, shown.ReceiverEmail);
        Assert.Equal((email.Id, true, (DateTimeOffset?)book.Clock.UtcNow), (Assert.Single(shown.Emails).Id, shown.Emails[0].ByTienditApp,
            shown.Emails[0].SentAt));
    }

    [Fact]
    public async Task Stamp_ShouldAskForNoEmail_WithoutAnAddress_OrWhenThePacRefusesTheInvoice()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidSaleAsync();

        book.Gateway.Refusal = "CFDI40145 - El nombre del receptor no coincide con el RFC.";
        Assert.Equal(OrderInvoiceStatus.Refused, (await book.IssueAsync(sale.Id, Karla)).Value.Status);
        book.Gateway.Refusal = null;
        Assert.Null((await book.IssueAsync(sale.Id)).Value.ReceiverEmail);

        Assert.Empty(await book.Context.CfdiEmails.ToListAsync());
        Assert.Equal(InvoiceEmailAddress.InvalidMessage,
            Assert.Single(new IssueOrderInvoiceCommandValidator().Validate(new IssueOrderInvoiceCommand(sale.Id, Receiver, false, "karla")).Errors)
                .ErrorMessage);
    }

    [Fact]
    public async Task ComplementsAndCreditNotes_ShouldGoToTheEmailTheirInvoiceHas_WhenTheyAreStamped()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync(downPayment: 50m);
        var invoice = (await book.IssueAsync(sale.Id, Karla)).Value;
        Assert.Equal(1, (await book.IssuePendingComplementsAsync()).Value);
        await book.WriteOffAsync(sale.Id, 50m);
        Assert.Equal(1, (await book.IssuePendingCreditNotesAsync()).Value);

        var complement = await book.Context.PaymentComplements.SingleAsync();
        var note = await book.Context.CreditNotes.SingleAsync();
        var emails = await book.Context.CfdiEmails.OrderBy(e => e.Document).ToListAsync();
        Assert.Equal(
            [
                (CfdiEmailDocument.Invoice, invoice.Id, Karla),
                (CfdiEmailDocument.PaymentComplement, complement.Id, Karla),
                (CfdiEmailDocument.CreditNote, note.Id, Karla),
            ],
            emails.Select(e => (e.Document, e.CfdiId, e.To)));
        Assert.All(emails, e => Assert.Equal(invoice.Id, e.OrderInvoiceId));

        foreach (var email in emails)
            await book.DeliverAsync(email.Id);
        Assert.Equal(
            ["Factura F-1 de Negocio demo", "Complemento de pago P-1 de Negocio demo", "Nota de crédito NC-1 de Negocio demo"],
            book.Email.Documents.Select(d => d.Subject));
        Assert.Contains("por el pago de $50.00", book.Email.Documents[1].HtmlBody);

        var invoicing = await book.InvoicingAsync(sale.Id);
        Assert.Equal(complement.Id, Assert.Single(Assert.Single(invoicing.Payments.SelectMany(p => p.Complements)).Emails).CfdiId);
        Assert.Equal(note.Id, Assert.Single(Assert.Single(invoicing.Adjustments.SelectMany(a => a.CreditNotes)).Emails).CfdiId);
        Assert.Equal(invoice.Id, Assert.Single(Assert.Single(invoicing.Invoices).Emails).CfdiId);

        // Each one is sent again by hand as what it is, with its own file.
        book.Clock.UtcNow = book.Clock.UtcNow.AddMinutes(1);
        var again = new[]
        {
            (await book.SendAsync(CfdiEmailDocument.PaymentComplement, complement.Id, "contabilidad@ejemplo.mx")).Value,
            (await book.SendAsync(CfdiEmailDocument.CreditNote, note.Id, "contabilidad@ejemplo.mx")).Value,
        };
        Assert.Equal(
            [(CfdiEmailDocument.PaymentComplement, complement.Id), (CfdiEmailDocument.CreditNote, note.Id)],
            again.Select(e => (e.Document, e.CfdiId)));
        foreach (var email in again)
            await book.DeliverAsync(email.Id);
        Assert.Equal(
            [$"P-1_{complement.Uuid}.xml", $"NC-1_{note.Uuid}.xml"],
            book.Email.Documents.TakeLast(2).Select(d => d.Attachments[0].FileName));
    }

    [Fact]
    public async Task Complement_OfAnInvoiceWithoutAnEmail_ShouldGoNowhere()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync(downPayment: 50m);
        await book.IssueAsync(sale.Id);

        Assert.Equal(1, (await book.IssuePendingComplementsAsync()).Value);

        Assert.Empty(await book.Context.CfdiEmails.ToListAsync());
    }

    [Fact]
    public async Task Send_ShouldAskForAnotherEmail_ToTheAddressGiven_AndKeepItForWhatFollows_WhenAsked()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync(downPayment: 50m);
        var invoice = (await book.IssueAsync(sale.Id)).Value;

        var once = await book.SendAsync(CfdiEmailDocument.Invoice, invoice.Id, " contabilidad@ejemplo.mx ");
        Assert.True(once.IsSuccess, once.Error);
        Assert.Equal(("contabilidad@ejemplo.mx", false, true), (once.Value.To, once.Value.ByTienditApp, once.Value.SentAt is null));
        var asked = await book.Context.CfdiEmails.SingleAsync();
        Assert.Equal(Guid.Parse(book.Fixture.CurrentUser.UserId!), asked.RequestedByUserId);
        // Only this once: what follows of the invoice still goes nowhere.
        Assert.Null((await book.Context.OrderInvoices.SingleAsync()).ReceiverEmail);

        book.Clock.UtcNow = book.Clock.UtcNow.AddMinutes(1);
        Assert.True((await book.SendAsync(CfdiEmailDocument.Invoice, invoice.Id, Karla, useForInvoice: true)).IsSuccess);
        Assert.Equal(Karla, (await book.Context.OrderInvoices.SingleAsync()).ReceiverEmail);
        Assert.Equal(1, (await book.IssuePendingComplementsAsync()).Value);
        var complement = await book.Context.PaymentComplements.SingleAsync();
        Assert.Equal(Karla, (await book.Context.CfdiEmails.SingleAsync(e => e.CfdiId == complement.Id)).To);

        // The latest ones show first.
        Assert.Equal([Karla, "contabilidad@ejemplo.mx"], Assert.Single((await book.InvoicingAsync(sale.Id)).Invoices).Emails.Select(e => e.To));
    }

    [Fact]
    public async Task Send_ShouldOnlyTakeAStampedCfdi_OfTheBusiness_ToOnePlainAddress()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidSaleAsync();
        book.Gateway.Unavailable = true;
        var stamping = (await book.IssueAsync(sale.Id)).Value;
        Assert.Equal(OrderInvoiceStatus.Stamping, stamping.Status);

        Assert.Equal(CfdiEmail.NotStampedMessage, (await book.SendAsync(CfdiEmailDocument.Invoice, stamping.Id, Karla)).Error);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => book.SendAsync(CfdiEmailDocument.Invoice, Guid.NewGuid(), Karla));
        // The id of an invoice is not a complement's.
        await Assert.ThrowsAsync<EntityNotFoundException>(() => book.SendAsync(CfdiEmailDocument.PaymentComplement, stamping.Id, Karla));
        Assert.Empty(await book.Context.CfdiEmails.ToListAsync());

        var validator = new SendCfdiByEmailCommandValidator();
        Assert.Equal(InvoiceEmailAddress.InvalidMessage,
            Assert.Single(validator.Validate(new SendCfdiByEmailCommand(CfdiEmailDocument.Invoice, stamping.Id, "Karla <karla@ejemplo.mx>", false)).Errors)
                .ErrorMessage);
        Assert.Equal("Escribe el correo al que se envía.",
            Assert.Single(validator.Validate(new SendCfdiByEmailCommand(CfdiEmailDocument.Invoice, stamping.Id, " ", false)).Errors).ErrorMessage);
        Assert.False(validator.Validate(new SendCfdiByEmailCommand((CfdiEmailDocument)7, stamping.Id, Karla, false)).IsValid);
        // The platform's SMTP sends seven-bit addresses: «peña@…» would never go out.
        Assert.Equal(InvoiceEmailAddress.InvalidMessage,
            Assert.Single(validator.Validate(new SendCfdiByEmailCommand(CfdiEmailDocument.Invoice, stamping.Id, "peña@ejemplo.mx", false)).Errors)
                .ErrorMessage);
    }

    [Fact]
    public async Task Send_ShouldStopAtFiveForOneCfdi_AndAtThreeHundredForTheBusiness_InADay()
    {
        await using var book = await Book.OpenAsync();
        var invoice = (await book.IssueAsync((await book.PaidSaleAsync()).Id, Karla)).Value;

        // The one TienditApp asked for counts.
        for (var i = 0; i < CfdiEmails.MaxPerCfdiPerDay - 1; i++)
        {
            book.Clock.UtcNow = book.Clock.UtcNow.AddMinutes(1);
            Assert.True((await book.SendAsync(CfdiEmailDocument.Invoice, invoice.Id, Karla)).IsSuccess);
        }
        Assert.Equal(CfdiEmails.TooManyForCfdiMessage("F-1"), (await book.SendAsync(CfdiEmailDocument.Invoice, invoice.Id, Karla)).Error);
        Assert.Equal(CfdiEmails.MaxPerCfdiPerDay, await book.Context.CfdiEmails.CountAsync());

        // A day after the first, one more goes.
        book.Clock.UtcNow = book.Clock.UtcNow.AddDays(1).AddMinutes(-3);
        Assert.True((await book.SendAsync(CfdiEmailDocument.Invoice, invoice.Id, Karla)).IsSuccess);

        // The business's day: another CFDI is not sent either.
        var stamped = await book.Context.OrderInvoices.AsNoTracking().SingleAsync();
        for (var i = await book.Context.CfdiEmails.CountAsync(e => e.RequestedAt > book.Clock.UtcNow - CfdiEmails.Day); i < CfdiEmails.MaxPerBusinessPerDay; i++)
            book.Context.CfdiEmails.Add(CfdiEmail.Request(stamped, Karla, null, book.Clock.UtcNow));
        await book.Context.SaveChangesAsync();
        var other = (await book.IssueAsync((await book.PaidSaleAsync()).Id)).Value;
        Assert.Equal(CfdiEmails.TooManyForBusinessMessage, (await book.SendAsync(CfdiEmailDocument.Invoice, other.Id, Karla)).Error);
    }

    [Fact]
    public async Task Queue_WithoutThePdf_ShouldWaitForIt_ThenSendTheXmlAlone_AndSaySo()
    {
        await using var book = await Book.OpenAsync();
        book.Gateway.PdfUnavailable = true;
        var invoice = (await book.IssueAsync((await book.PaidSaleAsync()).Id, Karla)).Value;
        Assert.False(invoice.HasPdf);
        var email = await book.Context.CfdiEmails.SingleAsync();

        // The first waits for the PDF the stamp keeps, without asking the PAC; the next one asks it.
        var asked = book.Gateway.PdfRequests;
        await Assert.ThrowsAsync<InvalidOperationException>(() => book.DeliverAsync(email.Id, attempt: 1));
        Assert.Equal(asked, book.Gateway.PdfRequests);
        await Assert.ThrowsAsync<InvalidOperationException>(() => book.DeliverAsync(email.Id, attempt: CfdiEmailRequestedHandler.AttemptsWaitingForPdf));
        Assert.Equal(asked + 1, book.Gateway.PdfRequests);
        // Waiting is not a try the mail server refused.
        var waiting = await book.Context.CfdiEmails.AsNoTracking().SingleAsync();
        Assert.Equal((0, true), (waiting.FailedAttempts, waiting.IsPending));
        Assert.Empty(book.Email.Documents);

        await book.DeliverAsync(email.Id, CfdiEmailRequestedHandler.AttemptsWaitingForPdf + 1);

        var sent = Assert.Single(book.Email.Documents);
        Assert.Equal(".xml", Path.GetExtension(Assert.Single(sent.Attachments).FileName));
        Assert.Contains("El PDF no estuvo disponible", sent.HtmlBody);
        Assert.True((await book.Context.CfdiEmails.AsNoTracking().SingleAsync()).SentWithoutPdf);

        // Once the PAC renders it, the next one carries it, rendered then (as downloading it does).
        book.Gateway.PdfUnavailable = false;
        var again = await book.SendAsync(CfdiEmailDocument.Invoice, invoice.Id, Karla);
        await book.DeliverAsync(again.Value.Id);
        Assert.Equal([".xml", ".pdf"], book.Email.Documents[^1].Attachments.Select(a => Path.GetExtension(a.FileName)));
        Assert.Equal(1, book.Gateway.PdfRenders);
    }

    [Fact]
    public async Task Queue_ShouldEndTheEmail_WithoutAProvider_OrWhenTheServerRefusesTheAddress()
    {
        await using var book = await Book.OpenAsync();
        var invoice = (await book.IssueAsync((await book.PaidSaleAsync()).Id, Karla)).Value;

        book.Email.DocumentsOutcome = EmailSendOutcome.NotConfigured;
        await book.DeliverAsync((await book.Context.CfdiEmails.SingleAsync()).Id);
        book.Email.DocumentsOutcome = EmailSendOutcome.Rejected;
        var refused = await book.SendAsync(CfdiEmailDocument.Invoice, invoice.Id, "nadie@ejemplo.mx");
        await book.DeliverAsync(refused.Value.Id);

        Assert.Equal(
            [(Karla, CfdiEmailFailure.NotConfigured), ("nadie@ejemplo.mx", CfdiEmailFailure.Rejected)],
            await book.Context.CfdiEmails.OrderBy(e => e.RequestedAt)
                .Select(e => new ValueTuple<string, CfdiEmailFailure?>(e.To, e.Failure)).ToListAsync());
        Assert.All(await book.Context.CfdiEmails.ToListAsync(), e => Assert.Equal((book.Clock.UtcNow, (DateTimeOffset?)null), (e.FailedAt!.Value, e.SentAt)));
    }

    [Fact]
    public async Task Queue_WhenTheServerDoesNotTakeIt_ShouldCountTheTry_AndThrow_ForTheOutboxToTryAgain()
    {
        await using var book = await Book.OpenAsync();
        await book.IssueAsync((await book.PaidSaleAsync()).Id, Karla);
        var email = await book.Context.CfdiEmails.SingleAsync();

        book.Email.DocumentsFailure = new IOException("The SMTP server could not be reached.");
        await Assert.ThrowsAsync<IOException>(() => book.DeliverAsync(email.Id));
        var tried = await book.Context.CfdiEmails.AsNoTracking().SingleAsync();
        Assert.Equal((1, (DateTimeOffset?)book.Clock.UtcNow, true), (tried.FailedAttempts, tried.LastFailedAttemptAt, tried.IsPending));

        book.Email.DocumentsFailure = null;
        await book.DeliverAsync(email.Id, attempt: 2);
        Assert.NotNull((await book.Context.CfdiEmails.AsNoTracking().SingleAsync()).SentAt);
        Assert.Single(book.Email.Documents);
    }

    [Fact]
    public async Task Queue_OnTheOutboxesLastAttempt_ShouldNotWaitForThePdf_AndEndTheEmailTheServerDidNotTake()
    {
        await using var book = await Book.OpenAsync();
        book.Gateway.PdfUnavailable = true;
        var invoice = (await book.IssueAsync((await book.PaidSaleAsync()).Id, Karla)).Value;

        // The only attempt is the last one: the XML goes alone rather than never.
        await book.DeliverAsync((await book.Context.CfdiEmails.SingleAsync()).Id, attempt: 1, maxAttempts: 1);
        Assert.True((await book.Context.CfdiEmails.AsNoTracking().SingleAsync()).SentWithoutPdf);

        book.Email.DocumentsFailure = new IOException("The SMTP server could not be reached.");
        book.Clock.UtcNow = book.Clock.UtcNow.AddMinutes(1);
        var again = (await book.SendAsync(CfdiEmailDocument.Invoice, invoice.Id, Karla)).Value;
        await Assert.ThrowsAsync<IOException>(() => book.DeliverAsync(again.Id, attempt: 7, maxAttempts: 8));
        // The outbox sets the message aside after this one: the email ends instead of saying it is on its way forever.
        await book.DeliverAsync(again.Id, attempt: 8, maxAttempts: 8);

        var ended = await book.Context.CfdiEmails.AsNoTracking().SingleAsync(e => e.Id == again.Id);
        Assert.Equal(((CfdiEmailFailure?)CfdiEmailFailure.NotDelivered, (DateTimeOffset?)book.Clock.UtcNow, 2, (DateTimeOffset?)null, false),
            (ended.Failure, ended.FailedAt, ended.FailedAttempts, ended.SentAt, ended.IsPending));
        Assert.Single(book.Email.Documents);
    }

    [Fact]
    public async Task Queue_ShouldNotSendACfdi_ThatIsNoLongerStamped()
    {
        await using var book = await Book.OpenAsync();
        var invoice = (await book.IssueAsync((await book.PaidSaleAsync()).Id, Karla)).Value;
        Assert.True((await book.CancelInvoiceAsync(invoice.Id)).IsSuccess);

        await book.DeliverAsync((await book.Context.CfdiEmails.SingleAsync()).Id);

        Assert.Empty(book.Email.Documents);
        Assert.Equal(CfdiEmailFailure.NoLongerStamped, (await book.Context.CfdiEmails.AsNoTracking().SingleAsync()).Failure);
    }

    [Fact]
    public async Task Stamp_ThatAnotherAttemptSavedFirst_ShouldLeaveOnlyThatAttemptsEmail()
    {
        var meanwhile = new StampedMeanwhile();
        await using var book = await Book.OpenAsync(meanwhile);
        meanwhile.Tenant = book.Fixture.TenantService;
        meanwhile.Armed = true;

        var invoice = (await book.IssueAsync((await book.PaidSaleAsync()).Id, Karla)).Value;

        Assert.Equal(OrderInvoiceStatus.Stamped, invoice.Status);
        var email = Assert.Single(await book.Context.CfdiEmails.ToListAsync());
        Assert.Equal(meanwhile.TheirsId, email.Id);
    }

    [Fact]
    public async Task Invoicing_ShouldProposeTheCustomersEmailForInvoices_ThenTheLastInvoices_ThenTheirEmail()
    {
        await using var book = await Book.OpenAsync(customerEmail: "Karla.Personal@ejemplo.mx");
        var sale = await book.PaidSaleAsync();
        Assert.Equal("Karla.Personal@ejemplo.mx", (await book.InvoicingAsync(sale.Id)).ProposedEmail);

        book.Gateway.Refusal = "CFDI40145 - El nombre del receptor no coincide con el RFC.";
        await book.IssueAsync(sale.Id, Karla);
        Assert.Equal(Karla, (await book.InvoicingAsync(sale.Id)).ProposedEmail);

        // Kept in the customer with their fiscal data, it goes first.
        book.Gateway.Refusal = null;
        Assert.True((await book.IssueAsync(sale.Id, "facturas@ejemplo.mx", saveToCustomer: true)).IsSuccess);
        Assert.Equal("facturas@ejemplo.mx", (await book.Context.Customers.AsNoTracking().SingleAsync()).InvoiceEmail);
        var next = await book.PaidSaleAsync();
        Assert.Equal("facturas@ejemplo.mx", (await book.InvoicingAsync(next.Id)).ProposedEmail);

        // Invoiced without one, keeping the data in the customer keeps theirs.
        Assert.True((await book.IssueAsync(next.Id, saveToCustomer: true)).IsSuccess);
        Assert.Equal("facturas@ejemplo.mx", (await book.Context.Customers.AsNoTracking().SingleAsync()).InvoiceEmail);
    }

    [Fact]
    public async Task Customer_ShouldKeepTheEmailForTheirInvoices_InClientes_AndTheAuditSayOnlyThatItChanged()
    {
        await using var book = await Book.OpenAsync();
        var handler = new UpdateCustomerFiscalDataCommandHandler(book.Context, book.Fixture.CurrentUser);

        var saved = await handler.Handle(new UpdateCustomerFiscalDataCommand(book.Customer.Id, Receiver, " facturas@ejemplo.mx "), CancellationToken.None);
        Assert.Equal("facturas@ejemplo.mx", saved.Value.InvoiceEmail);
        // The same again records nothing; the email alone, a change.
        await handler.Handle(new UpdateCustomerFiscalDataCommand(book.Customer.Id, Receiver, "facturas@ejemplo.mx"), CancellationToken.None);
        await handler.Handle(new UpdateCustomerFiscalDataCommand(book.Customer.Id, Receiver, "pagos@ejemplo.mx"), CancellationToken.None);
        // Left out (a client from before the Fase 28E), it stays; empty, it is forgotten.
        Assert.Equal("pagos@ejemplo.mx",
            (await handler.Handle(new UpdateCustomerFiscalDataCommand(book.Customer.Id, Receiver), CancellationToken.None)).Value.InvoiceEmail);
        Assert.Null((await handler.Handle(new UpdateCustomerFiscalDataCommand(book.Customer.Id, Receiver, ""), CancellationToken.None)).Value.InvoiceEmail);

        var changes = await book.Context.AuditLogs
            .Where(a => a.Action == CustomerFiscalDataAudit.Action)
            .OrderBy(a => a.Timestamp)
            .Select(a => a.Metadata)
            .ToListAsync();
        Assert.Equal(
            [
                """{"From":"Clientes","PreviousRfc":null,"Rfc":"XIQB891116QE4","InvoiceEmailChanged":true}""",
                """{"From":"Clientes","PreviousRfc":"XIQB891116QE4","Rfc":"XIQB891116QE4","InvoiceEmailChanged":true}""",
                """{"From":"Clientes","PreviousRfc":"XIQB891116QE4","Rfc":"XIQB891116QE4","InvoiceEmailChanged":true}""",
            ],
            changes);
        Assert.Equal(InvoiceEmailAddress.InvalidMessage,
            Assert.Single(new UpdateCustomerFiscalDataCommandValidator().Validate(new UpdateCustomerFiscalDataCommand(book.Customer.Id, Receiver, "pagos")).Errors)
                .ErrorMessage);
    }

    [Fact]
    public async Task Body_ShouldCarryTheBusinessesTextEncoded_AndTheSubjectInOneLine()
    {
        await using var book = await Book.OpenAsync();
        await book.IssueAsync((await book.PaidSaleAsync()).Id, Karla);
        var invoice = await book.Context.OrderInvoices.SingleAsync();

        var body = CfdiEmailRequestedHandler.Body(CfdiEmailDocument.Invoice, invoice, "Tacos <b>\"El Güero\"</b>\n& Co", withPdf: true);

        // Its text goes as text (what is not ASCII, as a character reference), the date in Spanish.
        Assert.Contains("Tacos &lt;b&gt;&quot;El G&#252;ero&quot;&lt;/b&gt; &amp; Co te envía la factura F-1 por $100.00, del 4 de octubre de 2026.", body);
        Assert.DoesNotContain("<b>", body);
        Assert.Contains("Emisor: ESCUELA KEMPER URGATE (RFC EKU9003173C9)", body);
        Assert.Contains("Receptor: KARLA FUENTE NOLASCO (RFC XIQB891116QE4)", body);
        Assert.Equal("Nota de crédito NC-4 de Tacos El Güero & Co",
            CfdiEmailRequestedHandler.Subject(CfdiEmailDocument.CreditNote, "NC-4", "Tacos El Güero\r\n& Co"));
    }

    /// <summary>
    /// The next save of a stamp finds that another attempt (the worker, with its own context) stamped the same invoice and
    /// asked for its email first: the database refuses this one as stale.
    /// </summary>
    private sealed class StampedMeanwhile : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public ICurrentTenantService? Tenant { get; set; }
        public Guid TheirsId { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var stamping = eventData.Context!.ChangeTracker.Entries<OrderInvoice>()
                .FirstOrDefault(entry => entry.State == EntityState.Modified && entry.Entity.Status == OrderInvoiceStatus.Stamped)?.Entity;
            if (!Armed || stamping is null)
                return result;
            Armed = false;

            await using var other = new SalesDbContext(
                (DbContextOptions<ApplicationDbContext>)eventData.Context.GetService<IDbContextOptions>(), Tenant!);
            var theirs = await other.OrderInvoices.SingleAsync(i => i.Id == stamping.Id, cancellationToken);
            theirs.Stamp(stamping.Uuid!, stamping.Uuid!, stamping.StampedAt!.Value, stamping.XmlFileKey!);
            var email = CfdiEmail.Request(theirs, theirs.ReceiverEmail, null, stamping.StampedAt.Value);
            other.CfdiEmails.Add(email);
            await other.SaveChangesAsync(cancellationToken);
            TheirsId = email.Id;
            throw new DbUpdateConcurrencyException("The invoice was stamped meanwhile.");
        }
    }

    private sealed class Book : IAsyncDisposable, ISender
    {
        private Book(SalesTestFixture fixture, Tenant tenant, Product product, Customer customer)
        {
            Fixture = fixture;
            Tenant = tenant;
            Product = product;
            Customer = customer;
        }

        public SalesTestFixture Fixture { get; }
        public Tenant Tenant { get; }
        public Product Product { get; }
        public Customer Customer { get; }
        public FakeInvoicingGateway Gateway { get; } = new();
        public MemoryPrivateStorage Storage { get; } = new();
        public RecordingEmailService Email { get; } = new();
        public SalesDbContext Context => Fixture.Context;
        public SalesClock Clock => Fixture.Clock;

        public static async Task<Book> OpenAsync(IInterceptor? interceptor = null, string? customerEmail = null)
        {
            var fixture = interceptor is null ? SalesTestFixture.Create() : SalesTestFixture.Create(null, interceptor);
            var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
            await fixture.OpenCashSessionAsync(tenant);
            var customer = new Customer(tenant.Id, "Karla", CustomerPhone, customerEmail);
            customer.SetCreditLimit(1_000m);
            fixture.Context.Customers.Add(customer);
            tenant.SetBusinessInfo(
                Rfc, "ESCUELA KEMPER URGATE", "Comercio", "601 - General de Ley Personas Morales",
                new Address("Reforma", "1", null, "Centro", "06000", "Cuauhtémoc", "Ciudad de México"));
            await fixture.Context.SaveChangesAsync();
            var book = new Book(fixture, tenant, await fixture.AddProductAsync(tenant, "Café", 100m), customer);
            var (cer, key) = TestCsd.Make(uniqueIdentifier: $"{Rfc} / VADA800927HSRSRL05");
            var uploaded = await new UploadInvoicingCertificateCommandHandler(
                    book.Context, fixture.TenantService, fixture.CurrentUser, book.Gateway, book.Clock,
                    NullLogger<UploadInvoicingCertificateCommandHandler>.Instance)
                .Handle(new UploadInvoicingCertificateCommand(Convert.ToBase64String(cer), Convert.ToBase64String(key), TestCsd.Password),
                    CancellationToken.None);
            Assert.True(uploaded.IsSuccess, uploaded.Error);
            return book;
        }

        /// <summary>A $100 sale to Karla, paid by card.</summary>
        public async Task<OrderDto> PaidSaleAsync()
        {
            var created = await Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                CustomerName = "Karla",
                CustomerPhone = CustomerPhone,
                Items = [new CreateOrderItemDto { ProductId = Product.Id, Quantity = 1 }],
                Payments = [new CreateOrderPaymentDto { Amount = 100m, Method = OrderPaymentMethod.Card }]
            }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return created.Value;
        }

        /// <summary>A $200 sale on credit to Karla (2 cafés, IVA included), with what she paid at the counter.</summary>
        public async Task<OrderDto> CreditSaleAsync(decimal downPayment)
        {
            var created = await Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                CustomerId = Customer.Id,
                OnCredit = true,
                Items = [new CreateOrderItemDto { ProductId = Product.Id, Quantity = 2 }],
                Payments = [new CreateOrderPaymentDto { Amount = downPayment, Method = OrderPaymentMethod.Cash }]
            }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return created.Value;
        }

        public async Task WriteOffAsync(Guid orderId, decimal amount)
        {
            Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
            var order = await Context.Orders.Include(o => o.Payments).Include(o => o.WriteOffs).SingleAsync(o => o.Id == orderId);
            order.WriteOff(amount, OrderWriteOffReason.WontBeCollected, null, Clock.UtcNow, "dueña");
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        public Task<OrderInvoicingDto> InvoicingAsync(Guid orderId)
            => new GetOrderInvoicingQueryHandler(Context, Fixture.TenantService, Gateway, new PhoneNormalizationService(), Clock)
                .Handle(new GetOrderInvoicingQuery(orderId), CancellationToken.None);

        public Task<Result<OrderInvoiceDto>> IssueAsync(Guid orderId, string? email = null, bool saveToCustomer = false)
            => new IssueOrderInvoiceCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssueOrderInvoiceCommand(orderId, Receiver, saveToCustomer, email), CancellationToken.None);

        public Task<Result<OrderInvoiceDto>> CancelInvoiceAsync(Guid invoiceId)
            => new CancelOrderInvoiceCommandHandler(Context, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new CancelOrderInvoiceCommand(invoiceId, CfdiCancellationMotive.ErrorsWithoutReplacement), CancellationToken.None);

        public Task<Result<int>> IssuePendingComplementsAsync()
            => new IssuePendingPaymentComplementsCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssuePendingPaymentComplementsCommand(), CancellationToken.None);

        public Task<Result<int>> IssuePendingCreditNotesAsync()
            => new IssuePendingCreditNotesCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssuePendingCreditNotesCommand(), CancellationToken.None);

        public async Task<Result<CfdiEmailDto>> SendAsync(CfdiEmailDocument document, Guid cfdiId, string email, bool useForInvoice = false)
        {
            var sent = await new SendCfdiByEmailCommandHandler(Context, Fixture.CurrentUser, new InvoicingLock(Context), Clock)
                .Handle(new SendCfdiByEmailCommand(document, cfdiId, email, useForInvoice), CancellationToken.None);
            Context.ChangeTracker.Clear();
            return sent;
        }

        /// <summary>The outbox runs the email's handler in its own scope, and saves what it changed when it ends well.</summary>
        public async Task DeliverAsync(Guid emailId, int attempt = 1, int maxAttempts = 8)
        {
            Context.ChangeTracker.Clear();
            var handler = new CfdiEmailRequestedHandler(Context, Storage, Gateway, Email, Clock, NullLogger<CfdiEmailRequestedHandler>.Instance);
            try
            {
                await handler.HandleAsync(
                    new CfdiEmailRequestedEvent(emailId),
                    new OutboxEventContext(Guid.NewGuid(), Tenant.Id, null, null, Clock.UtcNow, attempt, maxAttempts),
                    CancellationToken.None);
                await Context.SaveChangesAsync();
            }
            finally
            {
                Context.ChangeTracker.Clear();
            }
        }

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();

        // The requests go to their own handlers, as MediatR would send them.
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object response = request switch
            {
                RequestOrderInvoiceCommand command => await new RequestOrderInvoiceCommandHandler(
                        Context, Fixture.TenantService, Fixture.CurrentUser, new InvoicingLock(Context), new FolioGenerator(Context),
                        new PhoneNormalizationService(), Clock)
                    .Handle(command, cancellationToken),
                RequestAutomaticPaymentComplementCommand command => await new RequestAutomaticPaymentComplementCommandHandler(
                        Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, new InvoicingLock(Context), new FolioGenerator(Context), Clock)
                    .Handle(command, cancellationToken),
                RequestAutomaticCreditNoteCommand command => await new RequestAutomaticCreditNoteCommandHandler(
                        Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, new InvoicingLock(Context), new FolioGenerator(Context), Clock)
                    .Handle(command, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().Name)
            };
            return (TResponse)response;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
