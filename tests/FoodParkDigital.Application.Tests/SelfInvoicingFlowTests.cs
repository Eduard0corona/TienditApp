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
using FoodParkDigital.Infrastructure.Persistence;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Self-invoicing (Fase 28F, ADR 0050): the business turns it on, each ticket prints its folio and code, and the customer
/// invoices it on the business's page with them, its fiscal data and its email; the same invoice Pedidos makes, requested by
/// the customer, within the month the sale was paid. Without the right code nothing of the ticket is said, and a ticket
/// already invoiced has its invoice sent again only to whoever knows its RFC.
/// </summary>
public class SelfInvoicingFlowTests
{
    private const string Rfc = "EKU9003173C9";
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
    public async Task Business_ShouldTurnItOn_OnlyWhileItCanInvoice_AndKeepItsKeyWhenTurnedOff()
    {
        await using var book = await Book.OpenAsync(withCertificate: false);

        var refused = await book.SetSelfInvoicingAsync(true);
        Assert.Equal(InvoicingSettings.SelfInvoicingNeedsInvoicing(InvoicingSettings.NoCertificateMessage), refused.Error);
        Assert.False((await book.PageAsync())!.Available);

        await book.UploadCertificateAsync();
        var on = (await book.SetSelfInvoicingAsync(true)).Value;
        Assert.Equal((true, "/m/demo/facturar"), (on.SelfInvoicing, on.SelfInvoicePath));
        Assert.Equal(("Negocio demo", true), ((await book.PageAsync())!.BusinessName, (await book.PageAsync())!.Available));
        var key = (await book.Context.InvoicingProfiles.AsNoTracking().SingleAsync()).SelfInvoiceKey;

        var off = (await book.SetSelfInvoicingAsync(false)).Value;
        Assert.False(off.SelfInvoicing);
        Assert.False((await book.PageAsync())!.Available);
        Assert.Equal(key, (await book.Context.InvoicingProfiles.AsNoTracking().SingleAsync()).SelfInvoiceKey);
        Assert.Equal(
            ["SelfInvoicingTurnedOn", "SelfInvoicingTurnedOff"],
            await book.Context.AuditLogs.Where(a => a.Action.StartsWith("SelfInvoicing")).OrderBy(a => a.Timestamp).Select(a => a.Action).ToListAsync());
        // Off again changes nothing, and an unknown business has no page.
        Assert.True((await book.SetSelfInvoicingAsync(false)).IsSuccess);
        Assert.Equal(2, await book.Context.AuditLogs.CountAsync(a => a.Action.StartsWith("SelfInvoicing")));
        Assert.Null(await book.PageAsync("otro"));
    }

    [Fact]
    public async Task Ticket_ShouldPrintItsFolioAndCode_AndTheCustomerFindItAsTyped()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidSaleAsync();
        Assert.Null(await book.TicketCodeAsync(sale.Id));

        await book.SetSelfInvoicingAsync(true);
        var printed = (await book.TicketCodeAsync(sale.Id))!;
        Assert.Equal(("/m/demo/facturar", "ORD-000001"), (printed.Path, printed.OrderNumber));
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}$", printed.Code);

        foreach (var (folio, code) in new[] { ("ORD-000001", printed.Code), ("ord-000001", printed.Code.ToLowerInvariant()), ("1", printed.Code.Replace("-", " ")) })
        {
            var ticket = (await book.LookUpAsync(folio, code)).Value!;
            Assert.Equal(("ORD-000001", 100m, SelfInvoiceTicketStatus.CanInvoice, new DateOnly(2026, 10, 31), (string?)null),
                (ticket.OrderNumber, ticket.Total, ticket.Status, ticket.InvoiceBy, ticket.Message));
        }

        // Another sale's code, a code typed wrong, or a folio that is not there: nothing, the same for all.
        var other = await book.PaidSaleAsync();
        var otherCode = (await book.TicketCodeAsync(other.Id))!.Code;
        Assert.NotEqual(printed.Code, otherCode);
        foreach (var (folio, code) in new[] { ("ORD-000001", otherCode), ("ORD-000001", "AAAAA-AAAAA"), ("ORD-000009", printed.Code), ("X-1", printed.Code) })
        {
            var lookedUp = await book.LookUpAsync(folio, code);
            Assert.True(lookedUp.IsSuccess);
            Assert.Null(lookedUp.Value);
        }
        Assert.Null((await book.InvoiceAsync("ORD-000001", otherCode)).Value);
        Assert.Empty(await book.Context.OrderInvoices.ToListAsync());
    }

    [Fact]
    public async Task Customer_ShouldInvoiceTheTicket_AsPedidosDoes_AndGetItByEmail()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidSaleAsync();
        await book.SetSelfInvoicingAsync(true);
        var code = (await book.TicketCodeAsync(sale.Id))!.Code;

        var result = (await book.InvoiceAsync("ORD-000001", code, $" {Karla} ")).Value!;

        Assert.Equal((SelfInvoiceOutcome.Stamped, "F-1", Karla, (string?)null), (result.Outcome, result.InvoiceNumber, result.Email, result.Message));
        var invoice = await book.Context.OrderInvoices.SingleAsync();
        Assert.Equal((OrderInvoiceStatus.Stamped, OrderInvoice.SelfInvoiceRequester, true, "XIQB891116QE4", Karla),
            (invoice.Status, invoice.RequestedByUserId, invoice.RequestedByCustomer, invoice.Receiver.Rfc, invoice.ReceiverEmail));
        Assert.Equal((CfdiInvoice.PaidInOneExhibition, "04", 100m), (invoice.PaymentMethod, invoice.PaymentForm, invoice.Total));
        var email = Assert.Single(await book.Context.CfdiEmails.ToListAsync());
        Assert.Equal((invoice.Id, Karla, (Guid?)null), (email.CfdiId, email.To, email.RequestedByUserId));
        var audit = await book.Context.AuditLogs.SingleAsync(a => a.Action == "OrderInvoiceRequested");
        Assert.Equal(SelfInvoicing.PerformedBy, audit.PerformedBy);
        // Pedidos says the customer requested it; the customer's record keeps nothing of it.
        Assert.True(Assert.Single((await book.InvoicingAsync(sale.Id)).Invoices).RequestedByCustomer);
        Assert.Null((await book.Context.Customers.AsNoTracking().SingleAsync()).FiscalData);

        // The ticket says it has its invoice, and invoicing it again says so.
        var ticket = (await book.LookUpAsync("ORD-000001", code)).Value!;
        Assert.Equal((SelfInvoiceTicketStatus.Invoiced, "F-1"), (ticket.Status, ticket.InvoiceNumber));
        Assert.Equal(SelfInvoicing.AlreadyInvoicedMessage("F-1"), (await book.InvoiceAsync("ORD-000001", code)).Error);
        Assert.Single(await book.Context.OrderInvoices.ToListAsync());
    }

    [Fact]
    public async Task PacRefusal_ShouldReachTheCustomer_WhoCorrectsTheDataAndTriesAgain()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidSaleAsync();
        await book.SetSelfInvoicingAsync(true);
        var code = (await book.TicketCodeAsync(sale.Id))!.Code;

        book.Gateway.Refusal = "CFDI40145 - El nombre del receptor no coincide con el RFC.";
        var refused = (await book.InvoiceAsync("ORD-000001", code)).Value!;
        Assert.Equal((SelfInvoiceOutcome.Refused, "F-1", book.Gateway.Refusal), (refused.Outcome, refused.InvoiceNumber, refused.Message));
        Assert.Equal(SelfInvoiceTicketStatus.CanInvoice, (await book.LookUpAsync("ORD-000001", code)).Value!.Status);
        Assert.Empty(await book.Context.CfdiEmails.ToListAsync());

        book.Gateway.Refusal = null;
        var stamped = (await book.InvoiceAsync("ORD-000001", code)).Value!;
        Assert.Equal((SelfInvoiceOutcome.Stamped, "F-2"), (stamped.Outcome, stamped.InvoiceNumber));

        // While the PAC does not answer, the customer is told it follows by email.
        await using var other = await Book.OpenAsync();
        var otherSale = await other.PaidSaleAsync();
        await other.SetSelfInvoicingAsync(true);
        other.Gateway.Unavailable = true;
        var stamping = (await other.InvoiceAsync("ORD-000001", (await other.TicketCodeAsync(otherSale.Id))!.Code)).Value!;
        Assert.Equal(SelfInvoiceOutcome.Stamping, stamping.Outcome);
        Assert.Equal(SelfInvoiceTicketStatus.Stamping,
            (await other.LookUpAsync("ORD-000001", (await other.TicketCodeAsync(otherSale.Id))!.Code)).Value!.Status);
    }

    [Fact]
    public async Task Ticket_ShouldNotBeInvoicedOnline_UnpaidOrAfterItsMonth_OrWhileTheBusinessDoesNotOfferIt()
    {
        await using var book = await Book.OpenAsync();
        var paid = await book.PaidSaleAsync();
        var credit = await book.CreditSaleAsync(downPayment: 50m);
        await book.SetSelfInvoicingAsync(true);
        var paidCode = (await book.TicketCodeAsync(paid.Id))!.Code;
        var creditCode = (await book.TicketCodeAsync(credit.Id))!.Code;

        var unpaid = (await book.LookUpAsync(credit.OrderNumber, creditCode)).Value!;
        Assert.Equal(SelfInvoiceTicketStatus.NotOnline, unpaid.Status);
        Assert.Contains("todavía no está pagada por completo", unpaid.Message);
        Assert.Contains("todavía no está pagada por completo", (await book.InvoiceAsync(credit.OrderNumber, creditCode)).Error);

        // The month it was paid ends in the business's calendar: on November 1st it is asked of the business.
        book.Clock.UtcNow = new DateTimeOffset(2026, 11, 1, 5, 59, 0, TimeSpan.Zero);
        Assert.Equal(SelfInvoiceTicketStatus.CanInvoice, (await book.LookUpAsync("ORD-000001", paidCode)).Value!.Status);
        book.Clock.UtcNow = new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero);
        var late = (await book.LookUpAsync("ORD-000001", paidCode)).Value!;
        const string lateMessage = "Esta compra se podía facturar en línea hasta el sábado 31 de octubre de 2026: pídele tu factura al negocio.";
        Assert.Equal((SelfInvoiceTicketStatus.NotOnline, lateMessage), (late.Status, late.Message));
        Assert.Equal(lateMessage, (await book.InvoiceAsync("ORD-000001", paidCode)).Error);
        book.Clock.UtcNow = SalesTestFixture.Now;

        // Off, no ticket is read; on again, the same code works.
        await book.SetSelfInvoicingAsync(false);
        Assert.Equal(SelfInvoicing.NotOfferedMessage, (await book.LookUpAsync("ORD-000001", paidCode)).Error);
        Assert.Equal(SelfInvoicing.NotOfferedMessage, (await book.InvoiceAsync("ORD-000001", paidCode)).Error);
        Assert.Null(await book.TicketCodeAsync(paid.Id));
        await book.SetSelfInvoicingAsync(true);
        Assert.Equal(paidCode, (await book.TicketCodeAsync(paid.Id))!.Code);
        Assert.Equal(SelfInvoiceTicketStatus.CanInvoice, (await book.LookUpAsync("ORD-000001", paidCode)).Value!.Status);

        // Without its CSD the business cannot invoice now: the customer is told to come back or ask it.
        await book.RemoveCertificateAsync();
        Assert.Equal(SelfInvoicing.NotNowMessage, (await book.LookUpAsync("ORD-000001", paidCode)).Error);
        Assert.False((await book.PageAsync())!.Available);
        Assert.Empty(await book.Context.OrderInvoices.ToListAsync());
    }

    [Fact]
    public async Task Resend_ShouldNeedTheInvoicesRfc_AndKeepTheLimitsOfTheEmails()
    {
        await using var book = await Book.OpenAsync();
        var sale = await book.PaidSaleAsync();
        await book.SetSelfInvoicingAsync(true);
        var code = (await book.TicketCodeAsync(sale.Id))!.Code;
        Assert.Equal(SelfInvoicing.NoInvoiceToResendMessage, (await book.ResendAsync(code, Receiver.Rfc!, Karla)).Error);
        await book.InvoiceAsync("ORD-000001", code);

        Assert.Equal(SelfInvoicing.OtherRfcMessage, (await book.ResendAsync(code, Rfc, "otro@ejemplo.mx")).Error);
        Assert.Null((await book.ResendAsync("AAAAA-AAAAA", Receiver.Rfc!, "otro@ejemplo.mx")).Value);

        var resent = (await book.ResendAsync(code, " xiqb891116qe4 ", "otro@ejemplo.mx")).Value!;
        Assert.Equal(("F-1", "otro@ejemplo.mx"), (resent.InvoiceNumber, resent.Email));
        var email = await book.Context.CfdiEmails.SingleAsync(e => e.To == "otro@ejemplo.mx");
        Assert.Equal(OrderInvoice.SelfInvoiceRequester, email.RequestedByUserId);
        var shown = Assert.Single((await book.InvoicingAsync(sale.Id)).Invoices).Emails.Single(e => e.To == "otro@ejemplo.mx");
        Assert.Equal((true, false), (shown.ByCustomer, shown.ByTienditApp));

        // Five a day for one CFDI, the one TienditApp sent with the stamp included.
        for (var i = 0; i < CfdiEmails.MaxPerCfdiPerDay - 2; i++)
            Assert.True((await book.ResendAsync(code, Receiver.Rfc!, Karla)).IsSuccess);
        Assert.Equal(SelfInvoicing.TooManyForInvoiceMessage, (await book.ResendAsync(code, Receiver.Rfc!, Karla)).Error);
        Assert.Equal(CfdiEmails.MaxPerCfdiPerDay, await book.Context.CfdiEmails.CountAsync());
    }

    [Theory]
    [InlineData("ORD-000123", "ORD-000123")]
    [InlineData(" ord-000123 ", "ORD-000123")]
    [InlineData("123", "ORD-000123")]
    [InlineData("000123", "ORD-000123")]
    [InlineData("1234567", "ORD-1234567")]
    [InlineData("ORD 000123", "ORD-000123")]
    [InlineData("", null)]
    [InlineData("0", null)]
    [InlineData("ORD-", null)]
    [InlineData("F-123", null)]
    [InlineData("ORD-12A", null)]
    [InlineData("１２３", null)]
    [InlineData("9999999999999", null)]
    public void Folio_ShouldBeReadAsTheTicketPrintsIt(string typed, string? expected)
    {
        Assert.Equal(expected, SelfInvoicing.NormalizeFolio(typed));
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

        public static async Task<Book> OpenAsync(bool withCertificate = true)
        {
            var fixture = SalesTestFixture.Create();
            var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
            await fixture.OpenCashSessionAsync(tenant);
            var customer = new Domain.Customers.Entities.Customer(tenant.Id, "Karla", "+525512345678", null);
            customer.SetCreditLimit(1_000m);
            fixture.Context.Customers.Add(customer);
            tenant.SetBusinessInfo(
                Rfc, "ESCUELA KEMPER URGATE", "Comercio", "601 - General de Ley Personas Morales",
                new Address("Reforma", "1", null, "Centro", "06000", "Cuauhtémoc", "Ciudad de México"));
            await fixture.Context.SaveChangesAsync();
            var book = new Book(fixture, tenant, await fixture.AddProductAsync(tenant, "Café", 100m));
            if (withCertificate)
                await book.UploadCertificateAsync();
            return book;
        }

        public async Task UploadCertificateAsync()
        {
            var (cer, key) = TestCsd.Make(uniqueIdentifier: $"{Rfc} / VADA800927HSRSRL05");
            var uploaded = await new UploadInvoicingCertificateCommandHandler(
                    Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Clock,
                    NullLogger<UploadInvoicingCertificateCommandHandler>.Instance)
                .Handle(new UploadInvoicingCertificateCommand(Convert.ToBase64String(cer), Convert.ToBase64String(key), TestCsd.Password),
                    CancellationToken.None);
            Assert.True(uploaded.IsSuccess, uploaded.Error);
            Context.ChangeTracker.Clear();
        }

        public async Task RemoveCertificateAsync()
        {
            var removed = await new RemoveInvoicingCertificateCommandHandler(Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new RemoveInvoicingCertificateCommand(), CancellationToken.None);
            Assert.True(removed.IsSuccess, removed.Error);
            Context.ChangeTracker.Clear();
        }

        /// <summary>A $100 sale, paid by card.</summary>
        public async Task<OrderDto> PaidSaleAsync()
        {
            var created = await Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                CustomerName = "Karla",
                Items = [new CreateOrderItemDto { ProductId = Product.Id, Quantity = 1 }],
                Payments = [new CreateOrderPaymentDto { Amount = 100m, Method = OrderPaymentMethod.Card }]
            }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return created.Value;
        }

        /// <summary>A $200 sale on credit (2 cafés), with what was paid at the counter.</summary>
        public async Task<OrderDto> CreditSaleAsync(decimal downPayment)
        {
            var customer = await Context.Customers.AsNoTracking().SingleAsync();
            var created = await Fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                CustomerId = customer.Id,
                OnCredit = true,
                Items = [new CreateOrderItemDto { ProductId = Product.Id, Quantity = 2 }],
                Payments = [new CreateOrderPaymentDto { Amount = downPayment, Method = OrderPaymentMethod.Cash }]
            }, CancellationToken.None);
            Assert.True(created.IsSuccess, created.Error);
            return created.Value;
        }

        public async Task<Result<InvoicingSettingsDto>> SetSelfInvoicingAsync(bool on)
        {
            var set = await new SetSelfInvoicingCommandHandler(Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new SetSelfInvoicingCommand(on), CancellationToken.None);
            Context.ChangeTracker.Clear();
            return set;
        }

        /// <summary>What the ticket of the order prints, asked by the business (its own business in context).</summary>
        public async Task<OrderSelfInvoiceDto?> TicketCodeAsync(Guid orderId)
        {
            Fixture.TenantService.SetTenant(Tenant.Id);
            return await new GetOrderSelfInvoiceQueryHandler(Context, Fixture.TenantService)
                .Handle(new GetOrderSelfInvoiceQuery(orderId), CancellationToken.None);
        }

        public Task<OrderInvoicingDto> InvoicingAsync(Guid orderId)
        {
            Fixture.TenantService.SetTenant(Tenant.Id);
            return new GetOrderInvoicingQueryHandler(Context, Fixture.TenantService, Gateway, new PhoneNormalizationService(), Clock)
                .Handle(new GetOrderInvoicingQuery(orderId), CancellationToken.None);
        }

        // The public requests start without a business in context: the slug puts it.

        public Task<SelfInvoicePageDto?> PageAsync(string slug = "demo")
        {
            Fixture.TenantService.SetTenant(Guid.Empty);
            return new GetSelfInvoicePageQueryHandler(Context, Fixture.TenantService, Gateway, Clock)
                .Handle(new GetSelfInvoicePageQuery(slug), CancellationToken.None);
        }

        public Task<Result<SelfInvoiceTicketDto?>> LookUpAsync(string folio, string code)
        {
            Fixture.TenantService.SetTenant(Guid.Empty);
            return new LookUpSelfInvoiceTicketQueryHandler(Context, Fixture.TenantService, Gateway, Clock)
                .Handle(new LookUpSelfInvoiceTicketQuery("demo", folio, code), CancellationToken.None);
        }

        public async Task<Result<SelfInvoiceResultDto?>> InvoiceAsync(string folio, string code, string email = Karla)
        {
            Fixture.TenantService.SetTenant(Guid.Empty);
            var invoiced = await new SelfInvoiceCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new SelfInvoiceCommand("demo", folio, code, Receiver, email), CancellationToken.None);
            Context.ChangeTracker.Clear();
            return invoiced;
        }

        public async Task<Result<SelfInvoiceResendDto?>> ResendAsync(string code, string rfc, string email)
        {
            Fixture.TenantService.SetTenant(Guid.Empty);
            var resent = await new ResendSelfInvoiceCommandHandler(Context, Fixture.TenantService, Gateway, new InvoicingLock(Context), Clock)
                .Handle(new ResendSelfInvoiceCommand("demo", "ORD-000001", code, rfc, email), CancellationToken.None);
            Context.ChangeTracker.Clear();
            return resent;
        }

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();

        // The requests go to their own handlers, as MediatR would send them.
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object response = request switch
            {
                RequestSelfInvoiceCommand command => await new RequestSelfInvoiceCommandHandler(
                        Context, Fixture.TenantService, Gateway, new InvoicingLock(Context), new FolioGenerator(Context), Clock)
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
