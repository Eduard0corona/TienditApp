using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Orders.Queries;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Fiscal;
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
/// The global invoice of a period (Fase 28B, ADR 0046): what it takes and leaves out, its CFDI as the SAT's guide asks
/// (one concept per sale with a transfer per rate, InformacionGlobal, the general public), the sales it takes from an invoice
/// of their own until it is cancelled with motive 04, and the same stamping, retries and cancellation as an order's invoice.
/// </summary>
public class GlobalInvoicingFlowTests
{
    private const string Rfc = "EKU9003173C9";

    // The sales are made on Sunday 4 October 2026 (11:00 in Mexico City); the next morning the day is over.
    private static readonly DateOnly SaleDay = new(2026, 10, 4);
    private static readonly DateTimeOffset NextMorning = new(2026, 10, 5, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Draft_ShouldWaitForThePeriodToEnd_AndTakeTheSalesPaidInFullInIt_EachAConceptWithItsFolio()
    {
        await using var book = await Book.OpenAsync();
        var card = await book.PaidOrderAsync(2, OrderPaymentMethod.Card);
        var cash = await book.PaidOrderAsync(1, OrderPaymentMethod.Cash);
        var partly = await book.OrderAsync(1);
        await book.PayAsync(partly.Id, 50m);
        await book.OrderAsync(1);

        var today = await book.DraftAsync();
        Assert.Equal([GlobalInvoiceDrafts.NotEndedMessage(SaleDay)], today.Blockers);
        Assert.False(today.IsReadyToStamp);

        book.Clock.UtcNow = NextMorning;
        var draft = await book.DraftAsync();
        Assert.True(draft.IsReadyToStamp, string.Join(" ", draft.Blockers));
        Assert.Empty(draft.Warnings);
        Assert.Equal((GlobalInvoicePeriodicity.Daily, SaleDay, SaleDay, "10", 2026),
            (draft.Periodicity, draft.PeriodFrom, draft.PeriodTo, draft.Months, draft.Year));
        Assert.Equal([card.OrderNumber, cash.OrderNumber], draft.Sales.Select(sale => sale.OrderNumber));
        Assert.Equal((2, 2, 258.62m, 41.38m, 300m), (draft.SaleCount, draft.ConceptCount, draft.SubTotal, draft.TotalTransferred, draft.Total));
        // The form of the largest sale (the card one), not of most of the money.
        Assert.Equal("04", draft.PaymentForm);
        var transfer = Assert.Single(draft.Transfers);
        Assert.Equal((258.62m, "Tasa", 0.16m, 41.38m), (transfer.Base, transfer.FactorType, transfer.RateOrQuota, transfer.Amount));
        // Paid in part: it goes in the period it is paid in full. Without any payment it is not a sale of the period.
        var leftOut = Assert.Single(draft.LeftOut);
        Assert.Equal((partly.OrderNumber, GlobalInvoiceDrafts.NotPaidReason), (leftOut.OrderNumber, leftOut.Reason));
    }

    [Fact]
    public async Task Issue_ShouldStampItForTheGeneralPublic_WithInformacionGlobal_AndTakeItsSales()
    {
        await using var book = await Book.OpenAsync();
        var card = await book.PaidOrderAsync(2, OrderPaymentMethod.Card);
        var cash = await book.PaidOrderAsync(1, OrderPaymentMethod.Cash);
        book.Clock.UtcNow = NextMorning;

        var issued = await book.IssueAsync(2, 300m);

        Assert.True(issued.IsSuccess, issued.Error);
        Assert.Equal(("F-1", OrderInvoiceStatus.Stamped, 2, 300m), (issued.Value.Number, issued.Value.Status, issued.Value.OrderCount, issued.Value.Total));
        var (document, _) = Assert.Single(book.Gateway.StampRequests);
        Assert.Equal(("XAXX010101000", "PUBLICO EN GENERAL", "06000", "616", "S01"),
            (document.Receiver.Rfc, document.Receiver.Name, document.Receiver.PostalCode, document.Receiver.FiscalRegime, document.Receiver.CfdiUse));
        Assert.Equal(new CfdiDocumentGlobalInformation("01", "10", 2026), document.GlobalInformation);
        Assert.Equal(("04", "PUE", "2026-10-05T09:00:00"), (document.PaymentForm, document.PaymentMethod, document.Date));
        Assert.Equal([card.OrderNumber, cash.OrderNumber], document.Concepts.Select(concept => concept.Identification));
        Assert.All(document.Concepts, concept => Assert.Equal(("01010101", "ACT", "Venta", 1m, "02"),
            (concept.ProductCode, concept.UnitCode, concept.Description, concept.Quantity, concept.TaxObject)));
        Assert.Equal((172.41m, 27.59m), (document.Concepts[0].Amount, document.Concepts[0].AllTransfers.Single().Amount));
        Assert.Equal(300m, document.Total);

        // Its sales are taken: the next preview leaves them out, and they are not invoiced on their own.
        var again = await book.DraftAsync();
        Assert.Equal([GlobalInvoiceDrafts.NoSalesMessage], again.Blockers);
        Assert.All(again.LeftOut, sale => Assert.Equal(GlobalInvoiceDrafts.InGlobalReason("F-1"), sale.Reason));
        var own = await book.IssueOrderInvoiceAsync(card.Id);
        Assert.Equal(OrderInvoicing.InGlobalInvoiceMessage("F-1"), own.Error);
        Assert.Equal(OrderInvoicing.InGlobalInvoiceMessage("F-1"), (await book.OrderInvoicingAsync(card.Id)).Problem);

        var audit = await book.Context.AuditLogs.SingleAsync(a => a.Action == "GlobalInvoiceRequested");
        Assert.Contains("\"OrderCount\":2", audit.Metadata);
        var xml = await book.DownloadAsync(issued.Value.Id, OrderInvoiceFileKind.Xml);
        Assert.True(xml.IsSuccess, xml.Error);
        Assert.Equal($"F-1_{issued.Value.Uuid}.xml", xml.Value.FileName);
    }

    [Fact]
    public async Task CustomerAskingForTheirInvoice_ShouldCancelTheGlobalWith04_InvoiceTheSale_AndIssueTheGlobalAgainWithoutIt()
    {
        await using var book = await Book.OpenAsync();
        var card = await book.PaidOrderAsync(2, OrderPaymentMethod.Card);
        var cash = await book.PaidOrderAsync(1, OrderPaymentMethod.Cash);
        book.Clock.UtcNow = NextMorning;
        var first = (await book.IssueAsync(2, 300m)).Value;

        // The general public accepts nothing: the SAT cancels it at once, and only then are its sales free.
        book.Gateway.CancelOutcome = CfdiCancellationOutcome.Cancelled;
        var cancelled = await book.CancelAsync(first.Id, CfdiCancellationMotive.NominativeInGlobalInvoice);
        Assert.True(cancelled.IsSuccess, cancelled.Error);
        Assert.Equal(OrderInvoiceStatus.Cancelled, cancelled.Value.Status);
        Assert.Equal((Rfc, first.Uuid, CfdiCancellationMotive.NominativeInGlobalInvoice), book.Gateway.Cancellations.Single());
        Assert.True(await book.Context.GlobalInvoiceOrders.AllAsync(order => order.Released));

        var own = await book.IssueOrderInvoiceAsync(card.Id);
        Assert.True(own.IsSuccess, own.Error);
        Assert.Equal("F-2", own.Value.Number);

        var draft = await book.DraftAsync();
        Assert.Equal([cash.OrderNumber], draft.Sales.Select(sale => sale.OrderNumber));
        Assert.Equal(GlobalInvoiceDrafts.OwnInvoiceReason("F-2"), Assert.Single(draft.LeftOut).Reason);
        var second = await book.IssueAsync(1, 100m);
        Assert.Equal(("F-3", OrderInvoiceStatus.Stamped, 100m), (second.Value.Number, second.Value.Status, second.Value.Total));

        // Motive 03 is an order's, not a global invoice's.
        Assert.False((await book.CancelAsync(second.Value.Id, CfdiCancellationMotive.NotCarriedOut)).IsSuccess);

        // The list finds which global invoice took a sale, and one by its folio or UUID; the newest period first.
        Assert.Equal(["F-3", "F-1"], await book.ListAsync(null));
        Assert.Equal(["F-1"], await book.ListAsync(card.OrderNumber.ToLowerInvariant()));
        Assert.Equal(["F-3", "F-1"], await book.ListAsync(cash.OrderNumber));
        Assert.Equal(["F-3"], await book.ListAsync(" f-3 "));
        Assert.Equal(["F-3"], await book.ListAsync("3"));
        Assert.Equal(["F-1"], await book.ListAsync(first.Uuid!.ToLowerInvariant()));
        Assert.Empty(await book.ListAsync("X-3"));
        Assert.Empty(await book.ListAsync("F-2"));
    }

    [Fact]
    public async Task Issue_WhenTheSalesChangedSinceThePreview_ShouldStampNothing()
    {
        await using var book = await Book.OpenAsync();
        await book.PaidOrderAsync(2, OrderPaymentMethod.Card);
        book.Clock.UtcNow = NextMorning;

        Assert.Equal(GlobalInvoicing.SalesChangedMessage, (await book.IssueAsync(2, 200m)).Error);
        Assert.Equal(GlobalInvoicing.SalesChangedMessage, (await book.IssueAsync(1, 300m)).Error);
        Assert.Empty(book.Gateway.StampRequests);
        Assert.Empty(await book.Context.GlobalInvoices.ToListAsync());
    }

    [Fact]
    public async Task Refusal_ShouldFreeItsSales_ForTheNextGlobalInvoice()
    {
        await using var book = await Book.OpenAsync();
        await book.PaidOrderAsync(2, OrderPaymentMethod.Card);
        book.Clock.UtcNow = NextMorning;

        book.Gateway.Refusal = "CFDI40136 - El año no es válido.";
        var refused = await book.IssueAsync(1, 200m);
        Assert.Equal((OrderInvoiceStatus.Refused, "CFDI40136 - El año no es válido."), (refused.Value.Status, refused.Value.FailureMessage));
        Assert.True(await book.Context.GlobalInvoiceOrders.AllAsync(order => order.Released));

        book.Gateway.Refusal = null;
        var draft = await book.DraftAsync();
        Assert.Equal(1, draft.SaleCount);
        Assert.Equal(OrderInvoiceStatus.Stamped, (await book.IssueAsync(1, 200m)).Value.Status);
    }

    [Fact]
    public async Task LostAnswer_ShouldStayStamping_AndBeStampedAgainWithTheSameKey_ByThePersonOrTheWorker()
    {
        await using var book = await Book.OpenAsync();
        await book.PaidOrderAsync(2, OrderPaymentMethod.Card);
        book.Clock.UtcNow = NextMorning;

        book.Gateway.LoseNextAnswer = true;
        var pending = await book.IssueAsync(1, 200m);
        Assert.Equal(OrderInvoiceStatus.Stamping, pending.Value.Status);
        // Its sale stays taken while it may still be stamped.
        Assert.Equal(GlobalInvoiceDrafts.InGlobalReason("F-1"), Assert.Single((await book.DraftAsync()).LeftOut).Reason);

        book.Clock.UtcNow = NextMorning.AddMinutes(2);
        Assert.Equal(1, (await book.StampPendingAsync()).Value);
        var keys = book.Gateway.StampRequests.Select(request => request.Key).Distinct();
        Assert.Single(keys);
        var stamped = await book.Context.GlobalInvoices.AsNoTracking().SingleAsync();
        Assert.Equal((OrderInvoiceStatus.Stamped, "F-1"), (stamped.Status, stamped.Number));
        Assert.Equal(OrderInvoiceStatus.Stamped, (await book.RetryAsync(stamped.Id)).Value.Status);
    }

    [Fact]
    public async Task Concepts_OfASaleWithSeveralRates_ShouldBeOneConceptWithATransferPerRate_AndWhatIsNotSubjectAnother()
    {
        await using var book = await Book.OpenAsync();
        var tortillas = await book.Fixture.AddProductAsync(book.Tenant, "Tortillas", 20m, taxRate: 0m);
        var libro = await book.Fixture.AddProductAsync(book.Tenant, "Libro", 50m, isTaxExempt: true);
        var bag = await book.Fixture.AddProductAsync(book.Tenant, "Bolsa", 5m, taxRate: 0m);
        bag.SetFiscalData(null, null, CfdiTaxObject.NotSubject);
        await book.Context.SaveChangesAsync();
        var sale = await book.SaleAsync(OrderPaymentMethod.Cash, (book.Product, 1), (tortillas, 1), (libro, 1), (bag, 1));
        book.Clock.UtcNow = NextMorning;

        var issued = await book.IssueAsync(1, 175m);

        Assert.True(issued.IsSuccess, issued.Error);
        var document = book.Gateway.StampRequests.Single().Document;
        Assert.Equal(2, document.Concepts.Count);
        var subject = document.Concepts[0];
        Assert.Equal((sale.OrderNumber, "02", 156.21m, 156.21m), (subject.Identification, subject.TaxObject, subject.UnitValue, subject.Amount));
        Assert.Equal(
            [("Tasa", 0.16m, 86.21m, (decimal?)13.79m), ("Tasa", 0m, 20m, 0m), ("Exento", null, 50m, null)],
            subject.AllTransfers.Select(transfer => (transfer.FactorType, transfer.Rate, transfer.Base, transfer.Amount)));
        var notSubject = document.Concepts[1];
        Assert.Equal((sale.OrderNumber, "01", 5m), (notSubject.Identification, notSubject.TaxObject, notSubject.Amount));
        Assert.Empty(notSubject.AllTransfers);
        Assert.Equal((161.21m, 13.79m, 175m), (document.SubTotal, document.TotalTransferred, document.Total));
        Assert.Equal(3, document.Transfers.Count);
    }

    [Fact]
    public async Task Draft_ShouldLeaveOutSalesOnCredit_WithRefunds_OrWithAnInvoiceOfTheirOwn()
    {
        await using var book = await Book.OpenAsync();
        var invoiced = await book.PaidOrderAsync(1, OrderPaymentMethod.Card);
        Assert.True((await book.IssueOrderInvoiceAsync(invoiced.Id)).IsSuccess);
        var customer = await CreditTestKit.AddCustomerAsync(book.Fixture, book.Tenant, "Ana", "5511112222", creditLimit: 1_000m);
        var onCredit = await CreditTestKit.SellOnCreditAsync(book.Fixture, customer, book.Product, 1, SalesTestFixture.Now);
        await book.PayAsync(onCredit.Id, 100m);
        var refunded = await book.PaidOrderAsync(1, OrderPaymentMethod.Cash);
        var order = await book.Context.Orders.Include(o => o.Payments).SingleAsync(o => o.Id == refunded.Id);
        order.RegisterRefund(10m, OrderPaymentMethod.Cash, "Cambio", SalesTestFixture.Now, "cajero", null, null, false, []);
        await book.Context.SaveChangesAsync();
        book.Clock.UtcNow = NextMorning;

        var draft = await book.DraftAsync();

        Assert.Equal([GlobalInvoiceDrafts.NoSalesMessage], draft.Blockers);
        Assert.Equal(
            new Dictionary<string, string>
            {
                [invoiced.OrderNumber] = GlobalInvoiceDrafts.OwnInvoiceReason("F-1"),
                [onCredit.OrderNumber] = GlobalInvoiceDrafts.CreditReason,
                [refunded.OrderNumber] = GlobalInvoiceDrafts.RefundedReason
            },
            draft.LeftOut.ToDictionary(sale => sale.OrderNumber, sale => sale.Reason));
    }

    [Fact]
    public async Task Draft_ShouldSayWhatTheSatRefuses_AndWarnWhenItIsLate()
    {
        await using var book = await Book.OpenAsync();
        await book.PaidOrderAsync(1, OrderPaymentMethod.Other);
        await book.PaidOrderAsync(1, OrderPaymentMethod.Card);

        // Two as large: the form of the SAT; three days later, it is late.
        book.Clock.UtcNow = NextMorning.AddDays(2);
        var late = await book.DraftAsync();
        Assert.Equal(("04", true), (late.PaymentForm, late.IsReadyToStamp));
        Assert.Equal([GlobalInvoiceDrafts.LateWarning], late.Warnings);

        // The largest sale paid with «Otro» declares no form of the SAT.
        book.Clock.UtcNow = SalesTestFixture.Now;
        await book.PaidOrderAsync(3, OrderPaymentMethod.Other);
        book.Clock.UtcNow = NextMorning.AddDays(2);
        Assert.Contains(GlobalInvoiceDrafts.OtherPaymentBlocker, (await book.DraftAsync()).Blockers);

        // Two months only for the RIF (621); more than five years back, not at all.
        Assert.Contains(GlobalInvoiceDrafts.BimonthlyBlocker, (await book.DraftAsync(GlobalInvoicePeriodicity.Bimonthly)).Blockers);
        Assert.Contains(GlobalInvoiceDrafts.TooOldMessage(2021),
            (await book.DraftAsync(GlobalInvoicePeriodicity.Monthly, new DateOnly(2020, 12, 1))).Blockers);
    }

    [Fact]
    public async Task Draft_WithoutTheBusinessReady_ShouldSayWhy()
    {
        await using var book = await Book.OpenAsync(csd: false);
        await book.PaidOrderAsync(1, OrderPaymentMethod.Card);
        book.Clock.UtcNow = NextMorning;

        var draft = await book.DraftAsync();

        Assert.Equal(InvoicingSettings.NoCertificateMessage, draft.Problem);
        Assert.False(draft.IsReadyToStamp);
        Assert.Equal(InvoicingSettings.NoCertificateMessage, (await book.IssueAsync(1, 100m)).Error);
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
        /// <summary>Café, $100 with IVA.</summary>
        public Product Product { get; }
        public FakeInvoicingGateway Gateway { get; } = new();
        public MemoryPrivateStorage Storage { get; } = new();
        public SalesDbContext Context => Fixture.Context;
        public SalesClock Clock => Fixture.Clock;

        public static async Task<Book> OpenAsync(bool csd = true)
        {
            var fixture = SalesTestFixture.Create();
            var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
            await fixture.OpenCashSessionAsync(tenant);
            var book = new Book(fixture, tenant, await fixture.AddProductAsync(tenant, "Café", 100m));
            tenant.SetBusinessInfo(
                Rfc,
                "ESCUELA KEMPER URGATE",
                "Comercio",
                "601 - General de Ley Personas Morales",
                new Address("Reforma", "1", null, "Centro", "06000", "Cuauhtémoc", "Ciudad de México"));
            await book.Context.SaveChangesAsync();
            if (csd)
            {
                var (cer, key) = TestCsd.Make();
                var uploaded = await new UploadInvoicingCertificateCommandHandler(
                        book.Context, fixture.TenantService, fixture.CurrentUser, book.Gateway, book.Clock,
                        NullLogger<UploadInvoicingCertificateCommandHandler>.Instance)
                    .Handle(new UploadInvoicingCertificateCommand(Convert.ToBase64String(cer), Convert.ToBase64String(key), TestCsd.Password),
                        CancellationToken.None);
                Assert.True(uploaded.IsSuccess, uploaded.Error);
            }
            return book;
        }

        /// <summary>A sale of Café paid in full, today.</summary>
        public Task<OrderDto> PaidOrderAsync(int quantity, OrderPaymentMethod method)
            => SaleAsync(method, (Product, quantity));

        public async Task<OrderDto> SaleAsync(OrderPaymentMethod method, params (Product Product, int Quantity)[] lines)
        {
            var total = lines.Sum(line => line.Product.PriceAmount * line.Quantity);
            var created = await Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                CustomerName = "Público",
                Items = lines.Select(line => new CreateOrderItemDto { ProductId = line.Product.Id, Quantity = line.Quantity }).ToList(),
                Payments = [new CreateOrderPaymentDto { Amount = total, Method = method }]
            }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return created.Value;
        }

        /// <summary>A sale of Café without payments.</summary>
        public async Task<OrderDto> OrderAsync(int quantity)
        {
            var created = await Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                CustomerName = "Público",
                Items = [new CreateOrderItemDto { ProductId = Product.Id, Quantity = quantity }]
            }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return created.Value;
        }

        public async Task PayAsync(Guid orderId, decimal amount)
        {
            var paid = await Fixture.AddPaymentHandler().Handle(
                new AddOrderPaymentCommand { OrderId = orderId, Amount = amount, Method = OrderPaymentMethod.Cash }, CancellationToken.None);
            Assert.True(paid.IsSuccess, paid.Error);
        }

        public Task<GlobalInvoiceDraftDto> DraftAsync(GlobalInvoicePeriodicity periodicity = GlobalInvoicePeriodicity.Daily, DateOnly? day = null)
            => new GetGlobalInvoiceDraftQueryHandler(Context, Fixture.TenantService, Gateway, Clock)
                .Handle(new GetGlobalInvoiceDraftQuery(periodicity, day ?? SaleDay), CancellationToken.None);

        public Task<Result<GlobalInvoiceDto>> IssueAsync(int saleCount, decimal total)
            => new IssueGlobalInvoiceCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssueGlobalInvoiceCommand(GlobalInvoicePeriodicity.Daily, SaleDay, saleCount, total), CancellationToken.None);

        /// <returns>The folios the list shows for that search.</returns>
        public async Task<List<string>> ListAsync(string? search)
            => (await new GetGlobalInvoicesQueryHandler(Context).Handle(new GetGlobalInvoicesQuery(Search: search), CancellationToken.None))
                .Items.Select(invoice => invoice.Number).ToList();

        public Task<Result<GlobalInvoiceDto>> RetryAsync(Guid invoiceId)
            => new RetryGlobalInvoiceCommandHandler(Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new RetryGlobalInvoiceCommand(invoiceId), CancellationToken.None);

        public Task<Result<GlobalInvoiceDto>> CancelAsync(Guid invoiceId, CfdiCancellationMotive motive)
            => new CancelGlobalInvoiceCommandHandler(Context, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new CancelGlobalInvoiceCommand(invoiceId, motive), CancellationToken.None);

        public Task<Result<int>> StampPendingAsync()
            => new StampPendingInvoicesCommandHandler(Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new StampPendingInvoicesCommand(), CancellationToken.None);

        public Task<Result<PrivateFileDownload>> DownloadAsync(Guid invoiceId, OrderInvoiceFileKind kind)
            => new GetGlobalInvoiceFileQueryHandler(Context, Storage, Gateway, NullLogger<GetOrderInvoiceFileQueryHandler>.Instance)
                .Handle(new GetGlobalInvoiceFileQuery(invoiceId, kind), CancellationToken.None);

        public Task<Result<OrderInvoiceDto>> IssueOrderInvoiceAsync(Guid orderId)
            => new IssueOrderInvoiceCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssueOrderInvoiceCommand(orderId, new CfdiReceiverInput
                {
                    Rfc = "XIQB891116QE4",
                    Name = "Karla Fuente Nolasco",
                    PostalCode = "26015",
                    FiscalRegime = "612",
                    CfdiUse = "G03"
                }, SaveToCustomer: false), CancellationToken.None);

        public Task<OrderInvoicingDto> OrderInvoicingAsync(Guid orderId)
            => new GetOrderInvoicingQueryHandler(Context, Fixture.TenantService, Gateway, new PhoneNormalizationService(), Clock)
                .Handle(new GetOrderInvoicingQuery(orderId), CancellationToken.None);

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();

        // The requests the handlers send go to their own handlers, as MediatR would send them.
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => request switch
            {
                RequestGlobalInvoiceCommand global => (TResponse)(object)await new RequestGlobalInvoiceCommandHandler(
                        Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, new InvoicingLock(Context), new FolioGenerator(Context), Clock)
                    .Handle(global, cancellationToken),
                RequestOrderInvoiceCommand order => (TResponse)(object)await new RequestOrderInvoiceCommandHandler(
                        Context, Fixture.TenantService, Fixture.CurrentUser, new InvoicingLock(Context), new FolioGenerator(Context),
                        new PhoneNormalizationService(), Clock)
                    .Handle(order, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().Name)
            };

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
            => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
