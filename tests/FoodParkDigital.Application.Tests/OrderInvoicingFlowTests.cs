using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Customers.Commands;
using FoodParkDigital.Application.Finance;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Fiscal.Entities;
using FoodParkDigital.Domain.Fiscal.Enums;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The CFDI 4.0 invoice of an order (Fase 28A, ADR 0045): the business uploads its CSD, the invoice is fixed (folio, date,
/// key and the CFDI of the order's draft) before the PAC is asked, a lost answer is asked again with the same key, a refusal
/// frees the order, and a cancellation waits for the SAT.
/// </summary>
public class OrderInvoicingFlowTests
{
    private const string Rfc = "EKU9003173C9";
    private const string CustomerPhone = "5512345678";

    private static readonly CfdiReceiverInput Receiver = new()
    {
        Rfc = "xiqb891116qe4",
        Name = "Karla Fuente Nolasco",
        PostalCode = "26015",
        FiscalRegime = "612",
        CfdiUse = "G03"
    };

    [Fact]
    public async Task Settings_ShouldSayWhatIsMissing_UntilTheBusinessCanInvoice()
    {
        await using var book = await Book.OpenAsync(fiscalProfile: false, csd: false);

        book.Gateway.IsConfigured = false;
        var off = await book.SettingsAsync();
        Assert.False(off.Available);
        Assert.False(off.Ready);
        Assert.Equal(InvoicingSettings.NotAvailableMessage, InvoicingSettings.ReadinessProblem(off));

        book.Gateway.IsConfigured = true;
        var settings = await book.SettingsAsync();
        Assert.Equal(4, settings.MissingData.Count);
        Assert.Equal(OrderInvoice.DefaultSerie, settings.Serie);

        await book.SetFiscalProfileAsync();
        settings = await book.SettingsAsync();
        Assert.Empty(settings.MissingData);
        Assert.Null(settings.Certificate);
        Assert.Equal(InvoicingSettings.NoCertificateMessage, InvoicingSettings.ReadinessProblem(settings));

        var uploaded = await book.UploadCsdAsync();
        Assert.True(uploaded.IsSuccess, uploaded.Error);
        Assert.True(uploaded.Value.Ready);
        Assert.Equal((TestCsd.Number, Rfc, TestCsd.To), (uploaded.Value.Certificate!.Number, uploaded.Value.Certificate.Rfc, uploaded.Value.Certificate.ValidTo));

        var serie = await new ChangeInvoiceSerieCommandHandler(book.Context, book.Fixture.TenantService, book.Fixture.CurrentUser, book.Gateway, book.Clock)
            .Handle(new ChangeInvoiceSerieCommand(" fa "), CancellationToken.None);
        Assert.Equal("FA", serie.Value.Serie);
    }

    [Fact]
    public async Task Csd_ShouldBeCheckedBeforeThePacKeepsIt_AndNeverBeStored()
    {
        await using var book = await Book.OpenAsync(csd: false);

        var (cer, key) = TestCsd.Make();
        var wrongPassword = await book.UploadCsdAsync(cer, key, "otra");
        Assert.Equal(SealingCertificateReader.WrongPasswordMessage, wrongPassword.Error);
        var (otherCer, otherKey) = TestCsd.Make(uniqueIdentifier: "XIQB891116QE4");
        Assert.Equal(
            string.Format(UploadInvoicingCertificateCommandHandler.OtherRfcMessage, "XIQB891116QE4", Rfc),
            (await book.UploadCsdAsync(otherCer, otherKey)).Error);
        book.Clock.UtcNow = TestCsd.To.AddDays(1);
        Assert.Contains("venció", (await book.UploadCsdAsync(cer, key)).Error);
        book.Clock.UtcNow = SalesTestFixture.Now;
        Assert.Empty(book.Gateway.SavedCertificates);

        Assert.True((await book.UploadCsdAsync(cer, key)).IsSuccess);
        var saved = Assert.Single(book.Gateway.SavedCertificates);
        Assert.Equal(cer, saved.Certificate);
        Assert.Equal(key, saved.Key);
        Assert.Equal(TestCsd.Password, saved.Password);
        // Only what identifies it is kept here; the audit has neither the files nor the password.
        var audit = await book.Context.AuditLogs.SingleAsync(a => a.Action == "InvoicingCertificateUploaded");
        Assert.DoesNotContain(TestCsd.Password, audit.Metadata);
        Assert.DoesNotContain(Convert.ToBase64String(key), audit.Metadata);

        // A new one replaces it, and the PAC forgets the one before.
        var (renewedCer, renewedKey) = TestCsd.Make(number: "30001000000500003416");
        var renewed = await book.UploadCsdAsync(renewedCer, renewedKey);
        Assert.Equal("30001000000500003416", renewed.Value.Certificate!.Number);
        Assert.Equal([TestCsd.Number], book.Gateway.RemovedCertificates);

        var removed = await new RemoveInvoicingCertificateCommandHandler(
                book.Context, book.Fixture.TenantService, book.Fixture.CurrentUser, book.Gateway, book.Clock)
            .Handle(new RemoveInvoicingCertificateCommand(), CancellationToken.None);
        Assert.Null(removed.Value.Certificate);
        Assert.Equal([TestCsd.Number, "30001000000500003416"], book.Gateway.RemovedCertificates);
    }

    [Fact]
    public async Task Issue_ShouldStampAPaidOrder_WithTheAmountsOfItsDraft_AndKeepItsXmlAndPdf()
    {
        await using var book = await Book.OpenAsync();
        var order = await book.PaidOrderAsync();
        var draft = await new GetOrderCfdiDraftQueryHandler(book.Context)
            .Handle(new GetOrderCfdiDraftQuery { OrderId = order.Id, Receiver = Receiver }, CancellationToken.None);

        var issued = await book.IssueAsync(order.Id);

        Assert.True(issued.IsSuccess, issued.Error);
        var invoice = issued.Value;
        Assert.Equal((OrderInvoiceStatus.Stamped, "F-1"), (invoice.Status, invoice.Number));
        Assert.Equal((draft.Value.SubTotal, draft.Value.TotalTransferred, order.Total), (invoice.SubTotal, invoice.TaxTotal, invoice.Total));
        Assert.Equal(("XIQB891116QE4", "KARLA FUENTE NOLASCO", "PUE", "04"),
            (invoice.Receiver.Rfc, invoice.Receiver.Name, invoice.PaymentMethod, invoice.PaymentForm));
        var (document, key) = Assert.Single(book.Gateway.StampRequests);
        Assert.Equal(("F", "1", "2026-10-04T11:00:00", "06000"), (document.Serie, document.Folio, document.Date, document.ExpeditionPostalCode));
        Assert.Equal((draft.Value.SubTotal, draft.Value.TotalTransferred, draft.Value.Total), (document.SubTotal, document.TotalTransferred, document.Total));
        Assert.Equal(draft.Value.Concepts.Select(c => (c.ProductCode, c.Quantity, c.Amount)), document.Concepts.Select(c => (c.ProductCode, c.Quantity, c.Amount)));
        Assert.Equal(Rfc, document.Issuer.Rfc);

        var stored = await book.Context.OrderInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(key, stored.IdempotencyKey);
        Assert.Equal(book.Gateway.Stamped[key].Uuid, stored.Uuid);
        Assert.Equal(book.Gateway.Stamped[key].Xml, book.Storage.Text(stored.XmlFileKey!));
        Assert.Equal($"invoices/{book.Tenant.Id}/202610/{stored.Id}.xml", stored.XmlFileKey);
        Assert.True(book.Storage.Files.ContainsKey(stored.PdfFileKey!));
        Assert.True((invoice.HasXml, invoice.HasPdf) == (true, true));

        // One live invoice per order: it is cancelled before another one.
        var invoicing = await book.InvoicingAsync(order.Id);
        Assert.False(invoicing.CanInvoice);
        Assert.Equal("Este pedido ya tiene la factura F-1: cancélala para facturarlo otra vez.", invoicing.Problem);
        Assert.Equal(invoicing.Problem, (await book.IssueAsync(order.Id)).Error);
    }

    [Fact]
    public async Task Issue_WhenThePacAnswerIsLost_ShouldStayStamping_AndAskingAgainGetsTheSameStamp()
    {
        await using var book = await Book.OpenAsync();
        var order = await book.PaidOrderAsync();
        book.Gateway.LoseNextAnswer = true;

        var issued = await book.IssueAsync(order.Id);

        Assert.Equal(OrderInvoiceStatus.Stamping, issued.Value.Status);
        Assert.Equal(OrderInvoicing.LiveInvoiceProblem(await book.Context.OrderInvoices.AsNoTracking().SingleAsync()),
            (await book.InvoicingAsync(order.Id)).Problem);

        var retried = await book.RetryAsync(issued.Value.Id);

        Assert.Equal(OrderInvoiceStatus.Stamped, retried.Value.Status);
        Assert.Equal(2, book.Gateway.StampRequests.Count);
        // The same CFDI with the same key: the PAC gave back the stamp it made, never a second invoice.
        Assert.Single(book.Gateway.Stamped);
        Assert.Equal(book.Gateway.StampRequests[0].Key, book.Gateway.StampRequests[1].Key);
        Assert.Equal(book.Gateway.StampRequests[0].Document.ToJson(), book.Gateway.StampRequests[1].Document.ToJson());
        Assert.Equal(book.Gateway.Stamped.Values.Single().Uuid, retried.Value.Uuid);
        Assert.Equal("F-1", retried.Value.Number);
    }

    [Fact]
    public async Task Worker_ShouldAskAgainForInvoicesStillStamping_WithinThePacsWindow()
    {
        await using var book = await Book.OpenAsync();
        var order = await book.PaidOrderAsync();
        book.Gateway.Unavailable = true;
        var issued = await book.IssueAsync(order.Id);
        Assert.Equal(OrderInvoiceStatus.Stamping, issued.Value.Status);
        book.Gateway.Unavailable = false;

        // Too young: it may still be in the hands of whoever asked for it.
        Assert.Equal(0, (await book.StampPendingAsync()).Value);
        Assert.Empty(book.Gateway.StampRequests);

        book.Clock.UtcNow = SalesTestFixture.Now.AddMinutes(5);
        Assert.Equal(1, (await book.StampPendingAsync()).Value);
        Assert.Equal(OrderInvoiceStatus.Stamped, (await book.Context.OrderInvoices.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Worker_ShouldLeaveAloneInvoicesOlderThanThePacsWindow()
    {
        await using var book = await Book.OpenAsync();
        var order = await book.PaidOrderAsync();
        book.Gateway.Unavailable = true;
        await book.IssueAsync(order.Id);
        book.Gateway.Unavailable = false;

        book.Clock.UtcNow = SalesTestFixture.Now + StampPendingInvoicesCommandHandler.RetryWindow + TimeSpan.FromMinutes(1);
        Assert.Equal(0, (await book.StampPendingAsync()).Value);
        Assert.Empty(book.Gateway.StampRequests);
    }

    [Fact]
    public async Task Issue_WhenThePacRefuses_ShouldBeRefused_AndTheOrderInvoicedAgainWithTheNextFolio()
    {
        await using var book = await Book.OpenAsync();
        var order = await book.PaidOrderAsync();
        book.Gateway.Refusal = "CFDI40145 - El nombre del receptor no coincide con el RFC.";

        var refused = await book.IssueAsync(order.Id);

        Assert.Equal((OrderInvoiceStatus.Refused, "F-1"), (refused.Value.Status, refused.Value.Number));
        Assert.Equal(book.Gateway.Refusal, refused.Value.FailureMessage);
        var invoicing = await book.InvoicingAsync(order.Id);
        Assert.True(invoicing.CanInvoice);
        // The refused one's receiver is proposed again, to correct it.
        Assert.Equal("XIQB891116QE4", invoicing.ProposedReceiver!.Rfc);

        book.Gateway.Refusal = null;
        var stamped = await book.IssueAsync(order.Id);
        Assert.Equal((OrderInvoiceStatus.Stamped, "F-2"), (stamped.Value.Status, stamped.Value.Number));
        Assert.Equal(["F-2", "F-1"], (await book.InvoicingAsync(order.Id)).Invoices.Select(i => i.Number));
    }

    [Fact]
    public async Task Issue_ShouldOnlyTakeAPaidSale_WithNothingGivenBack_ToANamedCustomer_WithTheBusinessesCsd()
    {
        await using var book = await Book.OpenAsync(csd: false);
        var unpaid = await book.OrderAsync(paid: false);
        var paid = await book.PaidOrderAsync();

        Assert.Equal(InvoicingSettings.NoCertificateMessage, (await book.IssueAsync(paid.Id)).Error);
        Assert.Equal(InvoicingSettings.NoCertificateMessage, (await book.InvoicingAsync(paid.Id)).Problem);
        await book.UploadCsdAsync();

        Assert.Equal(OrderInvoicing.NotPaidMessage, (await book.IssueAsync(unpaid.Id)).Error);
        Assert.Equal(OrderInvoicing.NotPaidMessage, (await book.InvoicingAsync(unpaid.Id)).Problem);
        Assert.Contains("público en general", (await book.IssueAsync(paid.Id, Receiver with { Rfc = "XAXX010101000", FiscalRegime = "616", CfdiUse = "S01" })).Error);

        var refund = await new RefundOrderCommandHandler(
                book.Context, book.Fixture.CurrentUser, new OrderCashService(book.Context, book.Fixture.CurrentUser), book.Clock,
                NoOpOrderInventoryService.Instance)
            .Handle(new RefundOrderCommand { OrderId = paid.Id, Amount = 10m, Method = OrderPaymentMethod.Card, Reason = "Faltó uno", RestockItems = false },
                CancellationToken.None);
        Assert.True(refund.IsSuccess, refund.Error);
        Assert.Contains("nota de crédito", (await book.IssueAsync(paid.Id)).Error);

        book.Gateway.IsConfigured = false;
        Assert.Equal(InvoicingSettings.NotAvailableMessage, (await book.IssueAsync(paid.Id)).Error);
        Assert.Empty(book.Gateway.StampRequests);
        Assert.Empty(await book.Context.OrderInvoices.ToListAsync());
    }

    [Fact]
    public async Task Issue_ShouldKeepTheFiscalDataInTheCustomer_WhenAsked_AndProposeItNextTime()
    {
        await using var book = await Book.OpenAsync();
        var customer = new Customer(book.Tenant.Id, "Karla", "+52" + CustomerPhone, null);
        book.Context.Customers.Add(customer);
        await book.Context.SaveChangesAsync();
        var first = await book.PaidOrderAsync();
        var second = await book.PaidOrderAsync();
        Assert.Null((await book.InvoicingAsync(first.Id)).ProposedReceiver);
        Assert.Equal(customer.Id, (await book.InvoicingAsync(first.Id)).CustomerId);

        Assert.True((await book.IssueAsync(first.Id, saveToCustomer: true)).IsSuccess);

        var proposed = (await book.InvoicingAsync(second.Id)).ProposedReceiver!;
        Assert.Equal(("XIQB891116QE4", "KARLA FUENTE NOLASCO", "26015", "612", "G03"),
            (proposed.Rfc, proposed.Name, proposed.PostalCode, proposed.FiscalRegime, proposed.CfdiUse));

        // Edited or forgotten in Clientes.
        var forgotten = await new UpdateCustomerFiscalDataCommandHandler(book.Context)
            .Handle(new UpdateCustomerFiscalDataCommand(customer.Id, null), CancellationToken.None);
        Assert.Null(forgotten.Value.FiscalData);
        var publicGeneral = new Customer(book.Tenant.Id, "Público en general", null, null, isPublicGeneral: true);
        book.Context.Customers.Add(publicGeneral);
        await book.Context.SaveChangesAsync();
        Assert.Equal(Customer.PublicGeneralFiscalDataMessage, (await new UpdateCustomerFiscalDataCommandHandler(book.Context)
            .Handle(new UpdateCustomerFiscalDataCommand(publicGeneral.Id, Receiver), CancellationToken.None)).Error);
    }

    [Fact]
    public async Task Cancel_ShouldAskTheSatFirst_AndWaitForTheCustomer_UntilTheSatAnswers()
    {
        await using var book = await Book.OpenAsync();
        var order = await book.PaidOrderAsync();
        var invoice = (await book.IssueAsync(order.Id)).Value;

        book.Gateway.CancelRefusal = "El SAT no encuentra esta factura entre las del negocio.";
        Assert.Equal(book.Gateway.CancelRefusal, (await book.CancelAsync(invoice.Id)).Error);
        Assert.Equal(OrderInvoiceStatus.Stamped, (await book.Context.OrderInvoices.AsNoTracking().SingleAsync()).Status);
        book.Gateway.CancelRefusal = null;

        var cancelling = await book.CancelAsync(invoice.Id);

        Assert.Equal((OrderInvoiceStatus.Cancelling, CfdiCancellationMotive.NotCarriedOut), (cancelling.Value.Status, cancelling.Value.CancellationMotive));
        Assert.Equal((Rfc, invoice.Uuid!, CfdiCancellationMotive.NotCarriedOut), Assert.Single(book.Gateway.Cancellations));
        Assert.Contains("espera su cancelación", (await book.InvoicingAsync(order.Id)).Problem);

        // The SAT still waits for the customer.
        Assert.Equal(0, (await book.CheckCancellationsAsync()).Value);
        var query = Assert.Single(book.Gateway.StatusQueries);
        Assert.Equal((Rfc, "XIQB891116QE4", order.Total, invoice.Uuid, FakeInvoicingGateway.SealTail),
            (query.IssuerRfc, query.ReceiverRfc, query.Total, query.Uuid, query.SealTail));

        book.Gateway.SatStatus = new CfdiSatStatus(CfdiSatState.Cancelled, false);
        Assert.Equal(1, (await book.CheckCancellationsAsync()).Value);
        Assert.Equal(OrderInvoiceStatus.Cancelled, (await book.Context.OrderInvoices.AsNoTracking().SingleAsync()).Status);

        // Cancelled, the order is invoiced again with a new folio.
        Assert.Equal("F-2", (await book.IssueAsync(order.Id)).Value.Number);
    }

    [Fact]
    public async Task Cancel_RefusedByTheCustomer_ShouldKeepTheInvoiceStamped_WithTheReason()
    {
        await using var book = await Book.OpenAsync();
        var order = await book.PaidOrderAsync();
        var invoice = (await book.IssueAsync(order.Id)).Value;
        await book.CancelAsync(invoice.Id);
        book.Gateway.SatStatus = new CfdiSatStatus(CfdiSatState.InForce, CancellationRefused: true);

        Assert.Equal(1, (await book.CheckCancellationsAsync()).Value);

        var kept = await book.Context.OrderInvoices.AsNoTracking().SingleAsync();
        Assert.Equal((OrderInvoiceStatus.Stamped, CheckInvoiceCancellationsCommandHandler.RefusedByReceiverMessage), (kept.Status, kept.FailureMessage));
    }

    [Fact]
    public async Task Cancel_ThatTheSatAlreadyHad_ShouldCancelAtOnce()
    {
        await using var book = await Book.OpenAsync();
        var order = await book.PaidOrderAsync();
        var invoice = (await book.IssueAsync(order.Id)).Value;
        book.Gateway.CancelOutcome = CfdiCancellationOutcome.Cancelled;

        var cancelled = await book.CancelAsync(invoice.Id, CfdiCancellationMotive.ErrorsWithoutReplacement);

        Assert.Equal(OrderInvoiceStatus.Cancelled, cancelled.Value.Status);
        Assert.Equal("Esta factura ya está cancelada.", (await book.CancelAsync(invoice.Id)).Error);
    }

    [Fact]
    public async Task Download_ShouldGiveTheStampedXml_AndRenderThePdfWhenThePacDidNotGiveItWhenStamping()
    {
        await using var book = await Book.OpenAsync();
        var order = await book.PaidOrderAsync();
        book.Gateway.PdfUnavailable = true;
        var invoice = (await book.IssueAsync(order.Id)).Value;
        Assert.Equal((OrderInvoiceStatus.Stamped, false), (invoice.Status, invoice.HasPdf));

        var xml = await book.DownloadAsync(invoice.Id, OrderInvoiceFileKind.Xml);
        Assert.Equal(("application/xml", $"F-1_{invoice.Uuid}.xml"), (xml.Value.ContentType, xml.Value.FileName));
        Assert.Equal(GetOrderInvoiceFileQueryHandler.PdfUnavailableMessage, (await book.DownloadAsync(invoice.Id, OrderInvoiceFileKind.Pdf)).Error);

        book.Gateway.PdfUnavailable = false;
        var pdf = await book.DownloadAsync(invoice.Id, OrderInvoiceFileKind.Pdf);
        Assert.Equal(("application/pdf", $"F-1_{invoice.Uuid}.pdf"), (pdf.Value.ContentType, pdf.Value.FileName));
    }

    private sealed class Book : IAsyncDisposable, ISender
    {
        private Book(SalesTestFixture fixture, Tenant tenant, Product product)
        {
            Fixture = fixture;
            Tenant = tenant;
            Product = product;
        }

        public SalesTestFixture Fixture { get; }
        public Tenant Tenant { get; }
        public Product Product { get; }
        public FakeInvoicingGateway Gateway { get; } = new();
        public MemoryPrivateStorage Storage { get; } = new();
        public SalesDbContext Context => Fixture.Context;
        public SalesClock Clock => Fixture.Clock;

        public static async Task<Book> OpenAsync(bool fiscalProfile = true, bool csd = true)
        {
            var fixture = SalesTestFixture.Create();
            var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
            await fixture.OpenCashSessionAsync(tenant);
            var book = new Book(fixture, tenant, await fixture.AddProductAsync(tenant, "Café", 100m));
            if (fiscalProfile)
                await book.SetFiscalProfileAsync();
            if (csd)
                Assert.True((await book.UploadCsdAsync()).IsSuccess);
            return book;
        }

        public async Task SetFiscalProfileAsync()
        {
            Tenant.SetBusinessInfo(
                Rfc,
                "ESCUELA KEMPER URGATE",
                "Comercio",
                "601 - General de Ley Personas Morales",
                new Address("Reforma", "1", null, "Centro", "06000", "Cuauhtémoc", "Ciudad de México"));
            await Context.SaveChangesAsync();
        }

        public Task<Result<InvoicingSettingsDto>> UploadCsdAsync()
        {
            var (cer, key) = TestCsd.Make();
            return UploadCsdAsync(cer, key);
        }

        public Task<Result<InvoicingSettingsDto>> UploadCsdAsync(byte[] cer, byte[] key, string password = TestCsd.Password)
            => new UploadInvoicingCertificateCommandHandler(
                    Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Clock,
                    NullLogger<UploadInvoicingCertificateCommandHandler>.Instance)
                .Handle(new UploadInvoicingCertificateCommand(Convert.ToBase64String(cer), Convert.ToBase64String(key), password),
                    CancellationToken.None);

        public Task<InvoicingSettingsDto> SettingsAsync()
            => new GetInvoicingSettingsQueryHandler(Context, Fixture.TenantService, Gateway, Clock)
                .Handle(new GetInvoicingSettingsQuery(), CancellationToken.None);

        public Task<OrderDto> PaidOrderAsync() => OrderAsync(paid: true);

        public async Task<OrderDto> OrderAsync(bool paid)
        {
            var created = await Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                CustomerName = "Karla",
                CustomerPhone = CustomerPhone,
                Items = [new CreateOrderItemDto { ProductId = Product.Id, Quantity = 2 }],
                Payments = paid ? [new CreateOrderPaymentDto { Amount = 200m, Method = OrderPaymentMethod.Card }] : []
            }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return created.Value;
        }

        public Task<OrderInvoicingDto> InvoicingAsync(Guid orderId)
            => new GetOrderInvoicingQueryHandler(Context, Fixture.TenantService, Gateway, new PhoneNormalizationService(), Clock)
                .Handle(new GetOrderInvoicingQuery(orderId), CancellationToken.None);

        public Task<Result<OrderInvoiceDto>> IssueAsync(Guid orderId, CfdiReceiverInput? receiver = null, bool saveToCustomer = false)
            => new IssueOrderInvoiceCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssueOrderInvoiceCommand(orderId, receiver ?? Receiver, saveToCustomer), CancellationToken.None);

        public Task<Result<OrderInvoiceDto>> RetryAsync(Guid invoiceId)
            => new RetryOrderInvoiceCommandHandler(Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new RetryOrderInvoiceCommand(invoiceId), CancellationToken.None);

        public Task<Result<OrderInvoiceDto>> CancelAsync(Guid invoiceId, CfdiCancellationMotive motive = CfdiCancellationMotive.NotCarriedOut)
            => new CancelOrderInvoiceCommandHandler(Context, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new CancelOrderInvoiceCommand(invoiceId, motive), CancellationToken.None);

        public Task<Result<int>> StampPendingAsync()
            => new StampPendingInvoicesCommandHandler(Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new StampPendingInvoicesCommand(), CancellationToken.None);

        public Task<Result<int>> CheckCancellationsAsync()
            => new CheckInvoiceCancellationsCommandHandler(Context, Gateway, Storage, Clock, NullLogger<CheckInvoiceCancellationsCommandHandler>.Instance)
                .Handle(new CheckInvoiceCancellationsCommand(), CancellationToken.None);

        public Task<Result<PrivateFileDownload>> DownloadAsync(Guid invoiceId, OrderInvoiceFileKind kind)
            => new GetOrderInvoiceFileQueryHandler(Context, Storage, Gateway, NullLogger<GetOrderInvoiceFileQueryHandler>.Instance)
                .Handle(new GetOrderInvoiceFileQuery(invoiceId, kind), CancellationToken.None);

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();

        // The request of the invoice goes to its own handler, as MediatR would send it.
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is not RequestOrderInvoiceCommand command)
                throw new NotSupportedException(request.GetType().Name);
            var handler = new RequestOrderInvoiceCommandHandler(
                Context, Fixture.TenantService, Fixture.CurrentUser, new FolioGenerator(Context), new PhoneNormalizationService(), Clock);
            return (TResponse)(object)await handler.Handle(command, cancellationToken);
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
