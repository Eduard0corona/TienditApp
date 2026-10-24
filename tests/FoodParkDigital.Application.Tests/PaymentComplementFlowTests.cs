using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The invoice in installments of a sale not paid in full and the complement of each of its payments (Fase 28C, ADR 0047):
/// TienditApp issues them on its own, with the installment, the balance before and after and the IVA each payment covers;
/// a person issues one again after a refusal or cancels one; and the invoice is not cancelled while they stand.
/// </summary>
public class PaymentComplementFlowTests
{
    private const string Rfc = "EKU9003173C9";
    private const string CustomerPhone = "+525512345678";

    private static readonly CfdiReceiverInput Receiver = new()
    {
        Rfc = "xiqb891116qe4",
        Name = "Karla Fuente Nolasco",
        PostalCode = "26015",
        FiscalRegime = "612",
        CfdiUse = "G03"
    };

    [Fact]
    public async Task Issue_OfASaleNotPaidInFull_ShouldStampItInInstallments_OnlyWhenTheCustomerOwesIt()
    {
        await using var book = await Book.OpenAsync();
        var toCharge = await book.UnpaidSaleAsync();
        var credit = await book.CreditSaleAsync(downPayment: 50m);

        // A sale still to be charged at the counter is charged first: it is not invoiced in installments by mistake.
        Assert.Equal(OrderInvoicing.NotPaidMessage, (await book.IssueAsync(toCharge.Id)).Error);
        Assert.Equal(OrderInvoicing.NotPaidMessage, (await book.InvoicingAsync(toCharge.Id)).Problem);

        var draft = await book.DraftAsync(credit.Id);
        Assert.Equal(("PPD", "99"), (draft.PaymentMethod, draft.PaymentForm));
        Assert.Contains(GetOrderCfdiDraftQueryHandler.InstallmentsWarning, draft.Warnings);
        var invoice = await book.IssueAsync(credit.Id);

        Assert.True(invoice.IsSuccess, invoice.Error);
        Assert.Equal((OrderInvoiceStatus.Stamped, "PPD", "99", 200m), (invoice.Value.Status, invoice.Value.PaymentMethod, invoice.Value.PaymentForm, invoice.Value.Total));
        var sent = book.Gateway.StampRequests[^1].Document;
        Assert.Equal(("PPD", "99", "I"), (sent.PaymentMethod, sent.PaymentForm, sent.VoucherType));
        // Paid in full, it is still paid in one exhibition.
        var paid = await book.PaidSaleAsync();
        Assert.Equal("PUE", (await book.IssueAsync(paid.Id)).Value.PaymentMethod);
        Assert.Empty((await book.InvoicingAsync(paid.Id)).Payments);
    }

    [Fact]
    public async Task Worker_ShouldIssueTheComplementOfEachPayment_WithItsInstallmentBalancesAndProratedIva()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync(downPayment: 50m);
        var invoice = (await book.IssueAsync(sale.Id)).Value;

        // The down payment was received before the invoice: it gets its complement too, with its real date.
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);
        var first = Assert.Single(await book.Context.PaymentComplements.ToListAsync());
        Assert.Equal(("P-1", OrderInvoiceStatus.Stamped, 1, 200m, 50m, 150m, "01", true),
            (first.Number, first.Status, first.Installment, first.PreviousBalance, first.Amount, first.RemainingBalance, first.PaymentForm,
                first.RequestedByUserId is null));
        var document = book.Gateway.StampRequests[^1].Document;
        Assert.Equal(("P", "XXX", 0m, null, null, "CP01"),
            (document.VoucherType, document.Currency, document.Total, document.PaymentForm, document.PaymentMethod, document.Receiver.CfdiUse));
        Assert.Equal(("XIQB891116QE4", Rfc), (document.Receiver.Rfc, document.Issuer.Rfc));
        var payment = document.Payment!;
        // Paid at 17:00 UTC: 11:00 in Mexico City.
        Assert.Equal(("2026-10-04T11:00:00", "01", 50m), (payment.Date, payment.PaymentForm, payment.Amount));
        Assert.Equal((invoice.Uuid, "F", "1", 1, 200m, 50m, 150m, "02"),
            (payment.Invoice.Uuid, payment.Invoice.Serie, payment.Invoice.Folio, payment.Invoice.Installment, payment.Invoice.PreviousBalance,
                payment.Invoice.Paid, payment.Invoice.RemainingBalance, payment.Invoice.TaxObject));
        // A quarter of the $172.41 base and its IVA.
        var transfer = Assert.Single(payment.Invoice.Transfers);
        Assert.Equal((43.10m, 0.16m, 6.90m), (transfer.Base, transfer.Rate, transfer.Amount));

        // Nothing new: nothing is issued twice.
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);
        Assert.Single(await book.Context.PaymentComplements.ToListAsync());

        // The rest is paid: its complement settles the invoice and takes what is left of the IVA.
        await book.PayAsync(sale.Id, 150m, OrderPaymentMethod.Transfer);
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);
        var last = await book.Context.PaymentComplements.SingleAsync(c => c.Installment == 2);
        Assert.Equal(("P-2", 150m, 150m, 0m, "03"), (last.Number, last.PreviousBalance, last.Amount, last.RemainingBalance, last.PaymentForm));
        var settled = Assert.Single(book.Gateway.StampRequests[^1].Document.Payment!.Invoice.Transfers);
        Assert.Equal((129.31m, 20.69m), (settled.Base, settled.Amount));
        Assert.Equal(27.59m, transfer.Amount + settled.Amount);

        var invoicing = await book.InvoicingAsync(sale.Id);
        Assert.Equal([1, 2], invoicing.Payments.Select(p => p.Complements.Single().Installment));
        Assert.All(invoicing.Payments, p => Assert.False(p.CanIssue));
        Assert.Equal(new DateOnly(2026, 11, 5), invoicing.Payments[0].DueBy);
    }

    [Fact]
    public async Task Payment_WithoutAFormOfTheSat_ShouldGetNoComplement_AndSayWhy()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync();
        await book.IssueAsync(sale.Id);
        var other = await book.PayAsync(sale.Id, 20m, OrderPaymentMethod.Other);

        Assert.Equal(0, (await book.IssuePendingAsync()).Value);
        Assert.Empty(await book.Context.PaymentComplements.ToListAsync());
        var shown = Assert.Single((await book.InvoicingAsync(sale.Id)).Payments);
        Assert.Equal((false, PaymentComplements.OtherFormMessage), (shown.CanIssue, shown.Problem));
        Assert.Equal(PaymentComplements.OtherFormMessage, (await book.IssueComplementAsync(other.Id)).Error);
    }

    [Fact]
    public async Task Complement_ShouldBeIssuedOnlyForAStampedInvoiceInInstallments_ThatTheBusinessCanSealFor()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync(downPayment: 30m);
        var downPayment = (await book.Context.OrderPayments.SingleAsync()).Id;

        // Without an invoice, or while it is still stamping, there is nothing to relate the payment to.
        Assert.Equal(PaymentComplements.NoInvoiceInInstallmentsMessage, (await book.IssueComplementAsync(downPayment)).Error);
        book.Gateway.LoseNextAnswer = true;
        var stamping = (await book.IssueAsync(sale.Id)).Value;
        Assert.Equal(OrderInvoiceStatus.Stamping, stamping.Status);
        Assert.Contains("se está timbrando", (await book.IssueComplementAsync(downPayment)).Error);
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);
        await book.RetryInvoiceAsync(stamping.Id);

        // Without its CSD the business cannot seal it: the worker leaves it for later.
        await book.RemoveCsdAsync();
        Assert.Equal(InvoicingSettings.NoCertificateMessage, (await book.IssueComplementAsync(downPayment)).Error);
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);
        Assert.Equal(InvoicingSettings.NoCertificateMessage, (await book.InvoicingAsync(sale.Id)).Payments.Single().Problem);
        Assert.Empty(await book.Context.PaymentComplements.ToListAsync());

        await book.UploadCsdAsync();
        var issued = await book.IssueComplementAsync(downPayment);
        Assert.True(issued.IsSuccess, issued.Error);
        Assert.Equal((OrderInvoiceStatus.Stamped, false), (issued.Value.Status, issued.Value.IssuedAutomatically));
        // One live complement per payment.
        Assert.Contains("ya tiene el complemento P-1", (await book.IssueComplementAsync(downPayment)).Error);
    }

    [Fact]
    public async Task RefusedComplement_ShouldBeIssuedAgainOnlyByAPerson_WithAnotherFolio_AndTheSameInstallment()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync(downPayment: 50m);
        await book.IssueAsync(sale.Id);
        var payment = (await book.Context.OrderPayments.SingleAsync()).Id;

        book.Gateway.Refusal = "CRP20206 - La suma de ImpPagado no es igual a Monto.";
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);
        var refused = await book.Context.PaymentComplements.SingleAsync();
        Assert.Equal((OrderInvoiceStatus.Refused, book.Gateway.Refusal), (refused.Status, refused.FailureMessage));

        // The worker does not ask again on its own: a person does, once it is fixed.
        book.Gateway.Refusal = null;
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);
        var shown = Assert.Single((await book.InvoicingAsync(sale.Id)).Payments);
        Assert.True(shown.CanIssue);
        var again = await book.IssueComplementAsync(payment);
        Assert.Equal(("P-2", 1, OrderInvoiceStatus.Stamped), (again.Value.Number, again.Value.Installment, again.Value.Status));
        Assert.Equal(["P-2", "P-1"], (await book.InvoicingAsync(sale.Id)).Payments.Single().Complements.Select(c => c.Number));
    }

    [Fact]
    public async Task CancelledComplement_IssuedAgain_ShouldTakeBackItsInstallmentAndBalance_SoTheChainStaysWhole()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync(downPayment: 50m);
        await book.IssueAsync(sale.Id);
        var downPayment = (await book.Context.OrderPayments.SingleAsync()).Id;
        await book.IssuePendingAsync();
        await book.PayAsync(sale.Id, 150m, OrderPaymentMethod.Transfer);
        await book.IssuePendingAsync();
        var first = await book.Context.PaymentComplements.AsNoTracking().SingleAsync(c => c.Installment == 1);

        // The first one had an error: cancelled, the one issued again is installment 1 from $200 again, not a 3 from $0.
        book.Gateway.CancelOutcome = CfdiCancellationOutcome.Cancelled;
        Assert.True((await book.CancelComplementAsync(first.Id, CfdiCancellationMotive.ErrorsWithoutReplacement)).IsSuccess);
        var again = await book.IssueComplementAsync(downPayment);

        Assert.True(again.IsSuccess, again.Error);
        Assert.Equal(("P-3", 1, 200m, 50m, 150m), (again.Value.Number, again.Value.Installment, again.Value.PreviousBalance, again.Value.Amount,
            again.Value.RemainingBalance));
        var paid = book.Gateway.StampRequests[^1].Document.Payment!.Invoice;
        Assert.Equal((1, 200m, 150m), (paid.Installment, paid.PreviousBalance, paid.RemainingBalance));
        // It pays the last $50 the invoice owes: it takes what is left of the base and the IVA, so the complements add up to it.
        var transfer = Assert.Single(paid.Transfers);
        Assert.Equal((43.10m, 6.90m), (transfer.Base, transfer.Amount));
        Assert.Equal([(1, 200m, 150m), (2, 150m, 0m)], (await book.Context.PaymentComplements.AsNoTracking()
                .Where(c => c.Status == OrderInvoiceStatus.Stamped).OrderBy(c => c.Installment).ToListAsync())
            .Select(c => (c.Installment, c.PreviousBalance, c.RemainingBalance)));
    }

    [Fact]
    public async Task PaymentWhoseComplementWaitsToBeIssuedAgain_ShouldStillCount_InTheBalanceOfTheOnesIssuedMeanwhile()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync(downPayment: 50m);
        await book.IssueAsync(sale.Id);
        var downPayment = (await book.Context.OrderPayments.SingleAsync()).Id;
        await book.IssuePendingAsync();
        await book.PayAsync(sale.Id, 100m, OrderPaymentMethod.Transfer);
        await book.IssuePendingAsync();
        var first = await book.Context.PaymentComplements.AsNoTracking().SingleAsync(c => c.Installment == 1);
        book.Gateway.CancelOutcome = CfdiCancellationOutcome.Cancelled;
        Assert.True((await book.CancelComplementAsync(first.Id, CfdiCancellationMotive.ErrorsWithoutReplacement)).IsSuccess);

        // The last $50 arrive while the down payment's complement waits to be issued again: the $50 received then still
        // count, so the new one goes from $50 to $0, not from $150 to $100; and it takes its share of the IVA like any other.
        await book.PayAsync(sale.Id, 50m, OrderPaymentMethod.Card);
        Assert.Equal(1, (await book.IssuePendingAsync()).Value);
        var third = await book.Context.PaymentComplements.AsNoTracking().SingleAsync(c => c.Installment == 3);
        Assert.Equal((50m, 50m, 0m), (third.PreviousBalance, third.Amount, third.RemainingBalance));

        // Issued again, the down payment's takes back installment 1 from $200, and as the last one missing it takes what is
        // left of the base and the IVA: the complements that stand add up to the invoice.
        var again = await book.IssueComplementAsync(downPayment);
        Assert.Equal((1, 200m, 150m), (again.Value.Installment, again.Value.PreviousBalance, again.Value.RemainingBalance));
        var stamped = await book.Context.PaymentComplements.AsNoTracking()
            .Where(c => c.Status == OrderInvoiceStatus.Stamped).OrderBy(c => c.Installment).ToListAsync();
        Assert.Equal([(1, 200m, 150m), (2, 150m, 50m), (3, 50m, 0m)], stamped.Select(c => (c.Installment, c.PreviousBalance, c.RemainingBalance)));
        var transfers = stamped.SelectMany(c => CfdiDocument.FromJson(c.Document).Payment!.Invoice.Transfers).ToList();
        Assert.Equal((172.41m, 27.59m), (transfers.Sum(t => t.Base), transfers.Sum(t => t.Amount ?? 0m)));
    }

    [Fact]
    public async Task Payment_WithALiveComplementOfAnEarlierInvoice_ShouldNotGetAnother_AndSayWhichOne()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync(downPayment: 50m);
        var invoice = (await book.IssueAsync(sale.Id)).Value;
        var downPayment = (await book.Context.OrderPayments.SingleAsync()).Id;
        await book.IssuePendingAsync();
        // The invoice ended up cancelled while its complement was being stamped (the PAC answered both): a new invoice of
        // the order does not give the payment a second complement that stands.
        var cancelled = await book.Context.OrderInvoices.SingleAsync(i => i.Id == invoice.Id);
        book.Context.Entry(cancelled).Property(i => i.Status).CurrentValue = OrderInvoiceStatus.Cancelled;
        await book.Context.SaveChangesAsync();
        Assert.True((await book.IssueAsync(sale.Id)).IsSuccess);

        Assert.Equal("Este cobro ya tiene el complemento P-1: cancélalo para emitir otro.", (await book.IssueComplementAsync(downPayment)).Error);
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);
        Assert.Single(await book.Context.PaymentComplements.ToListAsync());
    }

    [Fact]
    public async Task Issue_OfASaleOwedThatCanStillChange_ShouldWaitUntilItCloses()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync();
        // Still pending, it can take items or a delivery: the invoice in installments would no longer match it.
        var order = await book.Context.Orders.SingleAsync(o => o.Id == sale.Id);
        book.Context.Entry(order).Property(o => o.Status).CurrentValue = OrderStatus.Pending;
        await book.Context.SaveChangesAsync();

        Assert.Equal(OrderInvoicing.StillChangingMessage, (await book.IssueAsync(sale.Id)).Error);
        Assert.Equal(OrderInvoicing.StillChangingMessage, (await book.InvoicingAsync(sale.Id)).Problem);
        Assert.Empty(book.Gateway.StampRequests);
    }

    [Fact]
    public async Task Invoice_ShouldNotBeCancelled_WhileItsPaymentsHaveComplements_UntilTheSatCancelsThem()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync(downPayment: 50m);
        var invoice = (await book.IssueAsync(sale.Id)).Value;
        await book.IssuePendingAsync();
        var complement = await book.Context.PaymentComplements.SingleAsync();

        var blocked = await book.CancelInvoiceAsync(invoice.Id);
        Assert.Equal(CancelOrderInvoiceCommandHandler.LiveComplementsMessage(["P-1"]), blocked.Error);
        Assert.Empty(book.Gateway.Cancellations);

        // The complement waits for the receiver to accept its cancellation, then the SAT cancels it.
        var cancelling = await book.CancelComplementAsync(complement.Id, CfdiCancellationMotive.ErrorsWithoutReplacement);
        Assert.Equal(OrderInvoiceStatus.Cancelling, cancelling.Value.Status);
        // Waiting for its cancellation it still stands with the SAT.
        Assert.Equal(CancelOrderInvoiceCommandHandler.LiveComplementsMessage(["P-1"]), (await book.CancelInvoiceAsync(invoice.Id)).Error);
        book.Gateway.SatStatus = new(CfdiSatState.Cancelled, false);
        Assert.Equal(1, (await book.CheckCancellationsAsync()).Value);
        Assert.Equal(OrderInvoiceStatus.Cancelled, (await book.Context.PaymentComplements.AsNoTracking().SingleAsync()).Status);
        // The status of a payment complement is asked with its total: zero.
        Assert.Equal(0m, book.Gateway.StatusQueries[^1].Total);

        var cancelled = await book.CancelInvoiceAsync(invoice.Id);
        Assert.True(cancelled.IsSuccess, cancelled.Error);
        // A cancelled complement does not come back on its own.
        Assert.Equal(0, (await book.IssuePendingAsync()).Value);
    }

    [Fact]
    public async Task Complement_ShouldOfferOnlyItsMotives_AndOnlyAStampedOneIsCancelled()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync(downPayment: 50m);
        await book.IssueAsync(sale.Id);
        await book.IssuePendingAsync();
        var complement = await book.Context.PaymentComplements.SingleAsync();

        var validator = new CancelPaymentComplementCommandValidator();
        Assert.False(validator.Validate(new CancelPaymentComplementCommand(complement.Id, CfdiCancellationMotive.ErrorsWithReplacement)).IsValid);
        Assert.False(validator.Validate(new CancelPaymentComplementCommand(complement.Id, CfdiCancellationMotive.NominativeInGlobalInvoice)).IsValid);
        Assert.True(validator.Validate(new CancelPaymentComplementCommand(complement.Id, CfdiCancellationMotive.NotCarriedOut)).IsValid);

        book.Gateway.CancelOutcome = CfdiCancellationOutcome.Cancelled;
        var cancelled = await book.CancelComplementAsync(complement.Id, CfdiCancellationMotive.NotCarriedOut);
        Assert.Equal(OrderInvoiceStatus.Cancelled, cancelled.Value.Status);
        Assert.Equal("Este complemento ya está cancelado.", (await book.CancelComplementAsync(complement.Id, CfdiCancellationMotive.NotCarriedOut)).Error);
    }

    [Fact]
    public async Task PaymentSerie_ShouldNameTheComplements_AndShareTheCounterOfASerieWithTheInvoices()
    {
        await using var book = await Book.OpenAsync();
        var settings = await book.ChangeSeriesAsync("F", "f");
        Assert.Equal(("F", "F"), (settings.Value.Serie, settings.Value.PaymentSerie));
        Assert.Contains("serie", (await book.ChangeSeriesAsync("F", "P-1")).Error);

        var sale = await book.CreditSaleAsync(downPayment: 50m);
        var invoice = (await book.IssueAsync(sale.Id)).Value;
        await book.IssuePendingAsync();

        // Both in serie F: the folio counts on, it never repeats.
        Assert.Equal("F-1", invoice.Number);
        Assert.Equal("F-2", (await book.Context.PaymentComplements.SingleAsync()).Number);
        // Changing only the invoices' serie leaves the complements' as it was.
        Assert.Equal(("FA", "F"), ((await book.ChangeSeriesAsync("FA")).Value.Serie, (await book.SettingsAsync()).PaymentSerie));
    }

    [Fact]
    public async Task Complement_OfAnOrderTheBusinessNoLongerSees_ShouldNotBeThere_AndEachActionAsksForItsAccess()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync(downPayment: 50m);
        await book.IssueAsync(sale.Id);
        await book.IssuePendingAsync();
        var payment = (await book.Context.OrderPayments.SingleAsync()).Id;
        var complement = (await book.Context.PaymentComplements.SingleAsync()).Id;

        // The business keeps only services: its sales are not there, and neither are their complements.
        (await book.Context.TenantSettings.SingleAsync()).UpdateCapabilities(TenantCapability.Service);
        await book.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<EntityNotFoundException>(() => book.IssueComplementAsync(payment));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => book.RetryComplementAsync(complement));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => book.CancelComplementAsync(complement, CfdiCancellationMotive.NotCarriedOut));
        Assert.Equal(GetOrderInvoiceFileQueryHandler.NotFoundMessage, (await book.DownloadComplementAsync(complement)).Error);

        Assert.Equal(TenantPermission.ManageOrders, Permission<IssuePaymentComplementCommand>());
        Assert.Equal(TenantPermission.ManageOrders, Permission<RequestPaymentComplementCommand>());
        Assert.Equal(TenantPermission.ManageOrders, Permission<RetryPaymentComplementCommand>());
        Assert.Equal(TenantPermission.ManageOrders, Permission<GetPaymentComplementFileQuery>());
        Assert.True(typeof(CancelPaymentComplementCommand).IsDefined(typeof(RequiresTenantManagerAttribute), false));
        Assert.True(typeof(IssuePendingPaymentComplementsCommand).IsDefined(typeof(AllowAnonymousRequestAttribute), false));
    }

    [Fact]
    public async Task Complement_Files_ShouldBeTheStampedXml_AndItsPdf()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.CreditSaleAsync(downPayment: 50m);
        await book.IssueAsync(sale.Id);
        await book.IssuePendingAsync();
        var complement = await book.Context.PaymentComplements.SingleAsync();

        var xml = await book.DownloadComplementAsync(complement.Id);
        var pdf = await book.DownloadComplementAsync(complement.Id, OrderInvoiceFileKind.Pdf);

        Assert.Equal(("application/xml", $"P-1_{complement.Uuid}.xml"), (xml.Value.ContentType, xml.Value.FileName));
        Assert.Equal("application/pdf", pdf.Value.ContentType);
        Assert.StartsWith($"invoices/{book.Tenant.Id}/", complement.XmlFileKey);
    }

    private static TenantPermission Permission<TRequest>()
        => Assert.Single(((RequiresPermissionAttribute)Attribute.GetCustomAttribute(typeof(TRequest), typeof(RequiresPermissionAttribute))!).AnyOf);

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
        public SalesDbContext Context => Fixture.Context;
        public SalesClock Clock => Fixture.Clock;

        public static async Task<Book> OpenAsync()
        {
            var fixture = SalesTestFixture.Create();
            var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
            await fixture.OpenCashSessionAsync(tenant);
            var customer = new Customer(tenant.Id, "Karla", CustomerPhone, null);
            customer.SetCreditLimit(1_000m);
            fixture.Context.Customers.Add(customer);
            tenant.SetBusinessInfo(
                Rfc, "ESCUELA KEMPER URGATE", "Comercio", "601 - General de Ley Personas Morales",
                new Address("Reforma", "1", null, "Centro", "06000", "Cuauhtémoc", "Ciudad de México"));
            await fixture.Context.SaveChangesAsync();
            var book = new Book(fixture, tenant, await fixture.AddProductAsync(tenant, "Café", 100m), customer);
            await book.UploadCsdAsync();
            return book;
        }

        public async Task UploadCsdAsync()
        {
            var (cer, key) = TestCsd.Make();
            var uploaded = await new UploadInvoicingCertificateCommandHandler(
                    Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Clock,
                    NullLogger<UploadInvoicingCertificateCommandHandler>.Instance)
                .Handle(new UploadInvoicingCertificateCommand(Convert.ToBase64String(cer), Convert.ToBase64String(key), TestCsd.Password),
                    CancellationToken.None);
            Assert.True(uploaded.IsSuccess, uploaded.Error);
        }

        public async Task RemoveCsdAsync()
        {
            var removed = await new RemoveInvoicingCertificateCommandHandler(Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new RemoveInvoicingCertificateCommand(), CancellationToken.None);
            Assert.True(removed.IsSuccess, removed.Error);
        }

        public Task<InvoicingSettingsDto> SettingsAsync()
            => new GetInvoicingSettingsQueryHandler(Context, Fixture.TenantService, Gateway, Clock)
                .Handle(new GetInvoicingSettingsQuery(), CancellationToken.None);

        public Task<Result<InvoicingSettingsDto>> ChangeSeriesAsync(string serie, string? paymentSerie = null)
            => new ChangeInvoiceSerieCommandHandler(Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new ChangeInvoiceSerieCommand(serie, paymentSerie), CancellationToken.None);

        /// <summary>A $200 sale on credit to Karla (2 cafés, IVA included), with what she paid at the counter.</summary>
        public async Task<OrderDto> CreditSaleAsync(decimal downPayment = 0m)
        {
            var created = await Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                CustomerId = Customer.Id,
                OnCredit = true,
                Items = [new CreateOrderItemDto { ProductId = Product.Id, Quantity = 2 }],
                Payments = downPayment > 0 ? [new CreateOrderPaymentDto { Amount = downPayment, Method = OrderPaymentMethod.Cash }] : []
            }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return created.Value;
        }

        public async Task<OrderDto> UnpaidSaleAsync()
        {
            var created = await Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                CustomerName = "Karla",
                CustomerPhone = CustomerPhone,
                Items = [new CreateOrderItemDto { ProductId = Product.Id, Quantity = 1 }]
            }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return created.Value;
        }

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

        /// <summary>
        /// A payment registered later (as an abono does): the clock moves a minute first, so payments never share their time
        /// and keep their order (two at the same instant would be ordered by their ids).
        /// </summary>
        public async Task<OrderPayment> PayAsync(Guid orderId, decimal amount, OrderPaymentMethod method)
        {
            Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
            var order = await Context.Orders.Include(o => o.Payments).SingleAsync(o => o.Id == orderId);
            var payment = order.RegisterPayment(amount, method, Clock.UtcNow, null, null, null, null);
            await Context.SaveChangesAsync();
            return payment;
        }

        public async Task<CfdiDraftDto> DraftAsync(Guid orderId)
        {
            var draft = await new GetOrderCfdiDraftQueryHandler(Context)
                .Handle(new GetOrderCfdiDraftQuery { OrderId = orderId, Receiver = Receiver }, CancellationToken.None);
            Assert.True(draft.IsSuccess, draft.Error);
            return draft.Value;
        }

        public Task<OrderInvoicingDto> InvoicingAsync(Guid orderId)
            => new GetOrderInvoicingQueryHandler(Context, Fixture.TenantService, Gateway, new PhoneNormalizationService(), Clock)
                .Handle(new GetOrderInvoicingQuery(orderId), CancellationToken.None);

        public Task<Result<OrderInvoiceDto>> IssueAsync(Guid orderId)
            => new IssueOrderInvoiceCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssueOrderInvoiceCommand(orderId, Receiver, false), CancellationToken.None);

        public Task<Result<OrderInvoiceDto>> RetryInvoiceAsync(Guid invoiceId)
            => new RetryOrderInvoiceCommandHandler(Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new RetryOrderInvoiceCommand(invoiceId), CancellationToken.None);

        public Task<Result<OrderInvoiceDto>> CancelInvoiceAsync(Guid invoiceId)
            => new CancelOrderInvoiceCommandHandler(Context, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new CancelOrderInvoiceCommand(invoiceId, CfdiCancellationMotive.ErrorsWithoutReplacement), CancellationToken.None);

        public Task<Result<PaymentComplementDto>> IssueComplementAsync(Guid paymentId)
            => new IssuePaymentComplementCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssuePaymentComplementCommand(paymentId), CancellationToken.None);

        public Task<Result<PaymentComplementDto>> RetryComplementAsync(Guid complementId)
            => new RetryPaymentComplementCommandHandler(Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new RetryPaymentComplementCommand(complementId), CancellationToken.None);

        public Task<Result<PaymentComplementDto>> CancelComplementAsync(Guid complementId, CfdiCancellationMotive motive)
            => new CancelPaymentComplementCommandHandler(Context, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new CancelPaymentComplementCommand(complementId, motive), CancellationToken.None);

        public Task<Result<PrivateFileDownload>> DownloadComplementAsync(Guid complementId, OrderInvoiceFileKind kind = OrderInvoiceFileKind.Xml)
            => new GetPaymentComplementFileQueryHandler(Context, Storage, Gateway, NullLogger<GetPaymentComplementFileQueryHandler>.Instance)
                .Handle(new GetPaymentComplementFileQuery(complementId, kind), CancellationToken.None);

        public Task<Result<int>> IssuePendingAsync()
            => new IssuePendingPaymentComplementsCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssuePendingPaymentComplementsCommand(), CancellationToken.None);

        public Task<Result<int>> CheckCancellationsAsync()
            => new CheckInvoiceCancellationsCommandHandler(Context, Gateway, Storage, Clock, NullLogger<CheckInvoiceCancellationsCommandHandler>.Instance)
                .Handle(new CheckInvoiceCancellationsCommand(), CancellationToken.None);

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
                RequestPaymentComplementCommand command => await new RequestPaymentComplementCommandHandler(
                        Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, new InvoicingLock(Context), new FolioGenerator(Context), Clock)
                    .Handle(command, cancellationToken),
                RequestAutomaticPaymentComplementCommand command => await new RequestAutomaticPaymentComplementCommandHandler(
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
