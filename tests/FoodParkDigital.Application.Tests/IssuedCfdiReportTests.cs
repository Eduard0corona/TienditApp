using System.IO.Compression;
using System.Text;
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
/// Reportes › CFDI emitidos (Fase 28H, ADR 0052) over CFDI stamped the way TienditApp stamps them: the month's list, its
/// summary and the download for the accountant, whose ZIP holds the XML the stamping kept and whose CSV says what each CFDI
/// asked the PAC for. Only owners and administrators; a month from 2022 on.
/// </summary>
public class IssuedCfdiReportTests
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
    public void Report_ShouldBeOnlyForOwnersAndAdministrators()
    {
        foreach (var query in new[] { typeof(GetIssuedCfdisQuery), typeof(GetIssuedCfdiSummaryQuery), typeof(ExportIssuedCfdisQuery) })
            Assert.True(query.IsDefined(typeof(RequiresTenantManagerAttribute), false), query.Name);
    }

    [Theory]
    [InlineData(2021, 12)]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(2101, 1)]
    public void Month_ShouldBeOneFrom2022(int year, int month)
    {
        Assert.Contains(IssuedCfdis.MonthMessage,
            new GetIssuedCfdisQueryValidator().Validate(new GetIssuedCfdisQuery(year, month)).Errors.Select(e => e.ErrorMessage));
        Assert.Contains(IssuedCfdis.MonthMessage,
            new GetIssuedCfdiSummaryQueryValidator().Validate(new GetIssuedCfdiSummaryQuery(year, month)).Errors.Select(e => e.ErrorMessage));
        Assert.Contains(IssuedCfdis.MonthMessage,
            new ExportIssuedCfdisQueryValidator().Validate(new ExportIssuedCfdisQuery(year, month)).Errors.Select(e => e.ErrorMessage));
        Assert.True(new GetIssuedCfdisQueryValidator().Validate(new GetIssuedCfdisQuery(2026, 10)).IsValid);
        Assert.False(new GetIssuedCfdisQueryValidator().Validate(new GetIssuedCfdisQuery(2026, 10, Search: new string('a', 101))).IsValid);
    }

    [Fact]
    public async Task Month_ShouldListTheInvoicesComplementsAndCreditNotesStamped_WithTheirReceiverOrderAndRelation()
    {
        await using var book = await Book.OpenAsync();
        var paid = await book.PaidSaleAsync();
        var invoice = (await book.IssueAsync(paid.Id)).Value;
        await book.RefundAsync(paid.Id, 20m, OrderPaymentMethod.Card);
        Assert.Equal(1, (await book.IssuePendingCreditNotesAsync()).Value);
        var onCredit = await book.CreditSaleAsync();
        var installments = (await book.IssueAsync(onCredit.Id)).Value;
        await book.PayAsync(onCredit.Id, 120m, OrderPaymentMethod.Transfer);
        Assert.Equal(1, (await book.IssuePendingComplementsAsync()).Value);

        var list = await book.ListAsync();

        Assert.Equal(
            [
                (IssuedCfdiKind.PaymentComplement, OrderInvoiceStatus.Stamped, installments.Number, onCredit.OrderNumber),
                (IssuedCfdiKind.OrderInvoice, OrderInvoiceStatus.Stamped, (string?)null, onCredit.OrderNumber),
                (IssuedCfdiKind.CreditNote, OrderInvoiceStatus.Stamped, invoice.Number, paid.OrderNumber),
                (IssuedCfdiKind.OrderInvoice, OrderInvoiceStatus.Stamped, null, paid.OrderNumber)
            ],
            list.Items.Select(c => (c.Kind, c.Status, c.RelatedNumber, c.OrderNumber)));
        Assert.All(list.Items, c => Assert.Equal(("XIQB891116QE4", "KARLA FUENTE NOLASCO", true), (c.ReceiverRfc, c.ReceiverName, c.HasXml)));
        // A part of the name finds them, whatever its case.
        Assert.Equal(4, (await book.ListAsync(search: "fuente")).TotalCount);
        var complement = list.Items[0];
        Assert.Equal(((decimal?)null, 120m, "03"), (complement.Total, complement.PaidAmount, complement.PaymentForm));
        Assert.Equal(("PPD", "99", 200m), (list.Items[1].PaymentMethod, list.Items[1].PaymentForm, list.Items[1].Total));

        var summary = await book.SummaryAsync();
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), (summary.From, summary.To));
        Assert.Equal(
            [(IssuedCfdiKind.OrderInvoice, 2, 300m), (IssuedCfdiKind.GlobalInvoice, 0, 0m), (IssuedCfdiKind.PaymentComplement, 1, 120m), (IssuedCfdiKind.CreditNote, 1, 20m)],
            summary.Kinds.Select(k => (k.Kind, k.InForce, k.Total)));

        // Another month has none of them.
        Assert.Empty((await book.ListAsync(month: 11)).Items);
    }

    [Fact]
    public async Task Download_ShouldZipTheXmlTheStampingKept_AndTheListWithWhatEachCfdiAskedThePacFor()
    {
        await using var book = await Book.OpenAsync();
        var paid = await book.PaidSaleAsync();
        var invoice = (await book.IssueAsync(paid.Id)).Value;
        await book.RefundAsync(paid.Id, 20m, OrderPaymentMethod.Card);
        Assert.Equal(1, (await book.IssuePendingCreditNotesAsync()).Value);
        var note = await book.Context.CreditNotes.SingleAsync();

        var downloaded = await book.ExportAsync();

        Assert.True(downloaded.IsSuccess, downloaded.Error);
        using var zip = new ZipArchive(downloaded.Value.Content, ZipArchiveMode.Read);
        Assert.Equal(
            [$"ingresos/{invoice.Number}_{invoice.Uuid}.xml", $"egresos/{note.Number}_{note.Uuid}.xml", "CFDI_2026-10.csv"],
            zip.Entries.Select(e => e.FullName));
        using (var kept = await book.Storage.OpenReadAsync(note.XmlFileKey!))
        using (var reader = new StreamReader(zip.GetEntry($"egresos/{note.Number}_{note.Uuid}.xml")!.Open()))
        using (var original = new StreamReader(kept!))
            Assert.Equal(await original.ReadToEndAsync(), await reader.ReadToEndAsync());

        using var csv = new StreamReader(zip.GetEntry("CFDI_2026-10.csv")!.Open(), Encoding.UTF8);
        var lines = (await csv.ReadToEndAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        // The credit note: the invoice's receiver with the use of a credit note, related to the invoice.
        var cells = lines[2].Split(',');
        Assert.Equal(("E", "Nota de crédito", "XIQB891116QE4", "G02", "04", "17.24", "2.76", "20.00", invoice.Uuid, paid.OrderNumber),
            (cells[0], cells[1], cells[10], cells[12], cells[14], cells[15], cells[16], cells[17], cells[19], cells[20]));
    }

    [Fact]
    public async Task Download_OfMoreCfdiThanItHolds_ShouldSaySo()
    {
        await using var book = await Book.OpenAsync();
        var first = await book.PaidSaleAsync();
        Assert.True((await book.IssueAsync(first.Id)).IsSuccess);
        var second = await book.PaidSaleAsync();
        Assert.True((await book.IssueAsync(second.Id)).IsSuccess);

        var downloaded = await book.ExportAsync(limit: 1);

        Assert.Equal(ExportIssuedCfdisQueryHandler.TooManyMessage(2, 1), downloaded.Error);
    }

    [Fact]
    public void List_ShouldWriteAsTextWhatASpreadsheetWouldRunAsAFormula_AndQuoteWhatHasCommas()
    {
        var row = new IssuedCfdiRow
        {
            Id = Guid.NewGuid(),
            Kind = IssuedCfdiKind.OrderInvoice,
            Serie = "F",
            Folio = 7,
            Status = OrderInvoiceStatus.Refused,
            IssuedAt = new DateTimeOffset(2026, 10, 4, 17, 0, 0, TimeSpan.Zero),
            ReceiverRfc = "XIQB891116QE4",
            ReceiverName = "=HYPERLINK(\"https://example.com\")",
            PaymentMethod = "PUE",
            PaymentForm = "01",
            SubTotal = 1000m,
            TaxTotal = 160m,
            Total = 1160m,
            OrderNumber = "@ORD-1, 2"
        };

        var text = Encoding.UTF8.GetString(IssuedCfdiCsv.Write([row], new Dictionary<Guid, string>(), TimeZoneInfo.Utc, xmlPaths: null));

        var line = text.Split("\r\n")[1];
        Assert.Equal(
            "I,Factura,F,7,,2026-10-04 17:00:00,,Rechazado por el PAC,,,XIQB891116QE4,\"'=HYPERLINK(\"\"https://example.com\"\")\",,PUE,01," +
            "1000.00,160.00,1160.00,,,\"'@ORD-1, 2\",",
            line);
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
            var (cer, key) = TestCsd.Make(uniqueIdentifier: $"{Rfc} / VADA800927HSRSRL05");
            var uploaded = await new UploadInvoicingCertificateCommandHandler(
                    book.Context, fixture.TenantService, fixture.CurrentUser, book.Gateway, book.Clock,
                    NullLogger<UploadInvoicingCertificateCommandHandler>.Instance)
                .Handle(new UploadInvoicingCertificateCommand(Convert.ToBase64String(cer), Convert.ToBase64String(key), TestCsd.Password),
                    CancellationToken.None);
            Assert.True(uploaded.IsSuccess, uploaded.Error);
            return book;
        }

        /// <summary>A $200 sale on credit to Karla (2 cafés, IVA included).</summary>
        public async Task<OrderDto> CreditSaleAsync()
        {
            var created = await Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                CustomerId = Customer.Id,
                OnCredit = true,
                Items = [new CreateOrderItemDto { ProductId = Product.Id, Quantity = 2 }]
            }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return created.Value;
        }

        /// <summary>A $100 sale to Karla, paid by card; the clock moves a minute first, so nothing shares its time.</summary>
        public async Task<OrderDto> PaidSaleAsync()
        {
            Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
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

        public async Task PayAsync(Guid orderId, decimal amount, OrderPaymentMethod method)
        {
            Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
            var order = await Context.Orders.Include(o => o.Payments).SingleAsync(o => o.Id == orderId);
            order.RegisterPayment(amount, method, Clock.UtcNow, null, null, null, null);
            await Context.SaveChangesAsync();
        }

        public async Task RefundAsync(Guid orderId, decimal amount, OrderPaymentMethod method)
        {
            Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
            var order = await Context.Orders.Include(o => o.Payments).Include(o => o.Refunds).Include(o => o.Items).SingleAsync(o => o.Id == orderId);
            order.RegisterRefund(amount, method, "Devolución de un café", Clock.UtcNow, "cajero", null, null, false, []);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        public async Task<Result<OrderInvoiceDto>> IssueAsync(Guid orderId)
        {
            Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
            return await new IssueOrderInvoiceCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssueOrderInvoiceCommand(orderId, Receiver, false), CancellationToken.None);
        }

        public Task<Result<int>> IssuePendingCreditNotesAsync()
            => new IssuePendingCreditNotesCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssuePendingCreditNotesCommand(), CancellationToken.None);

        public Task<Result<int>> IssuePendingComplementsAsync()
            => new IssuePendingPaymentComplementsCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssuePendingPaymentComplementsCommand(), CancellationToken.None);

        public Task<PaginatedList<IssuedCfdiDto>> ListAsync(int month = 10, string? search = null)
            => new GetIssuedCfdisQueryHandler(Context, Fixture.TenantService)
                .Handle(new GetIssuedCfdisQuery(2026, month, Search: search), CancellationToken.None);

        public Task<IssuedCfdiSummaryDto> SummaryAsync()
            => new GetIssuedCfdiSummaryQueryHandler(Context, Fixture.TenantService)
                .Handle(new GetIssuedCfdiSummaryQuery(2026, 10), CancellationToken.None);

        public Task<Result<PrivateFileDownload>> ExportAsync(int limit = ExportIssuedCfdisQueryHandler.MaxCount)
            => new ExportIssuedCfdisQueryHandler(Context, Fixture.TenantService, Storage, NullLogger<ExportIssuedCfdisQueryHandler>.Instance)
                {
                    Limit = limit
                }
                .Handle(new ExportIssuedCfdisQuery(2026, 10), CancellationToken.None);

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
