using System.IO.Compression;
using FoodParkDigital.Application.Common.Events;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Utils;
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
using FoodParkDigital.Domain.Fiscal.Events;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The CFDI of a month to the business's accountant by email (Fase 28O, ADR 0060), over CFDI stamped the way TienditApp stamps
/// them: the accountant's email in Configuración › Facturación, «Mandar al contador» in Reportes › CFDI emitidos, the email
/// the outbox sends with the same file as the download, and the month before that TienditApp queues on its own at the start
/// of each month. Owners and administrators only; two businesses never see each other's.
/// </summary>
public class AccountantCfdiEmailFlowTests
{
    private const string Rfc = "EKU9003173C9";
    private const string CustomerPhone = "+525512345678";
    private const string Accountant = "contador@despacho.mx";

    private static readonly CfdiReceiverInput Receiver = new()
    {
        Rfc = "xiqb891116qe4",
        Name = "Karla Fuente Nolasco",
        PostalCode = "26015",
        FiscalRegime = "612",
        CfdiUse = "G03"
    };

    // The first of November of 2026 in Mexico City (UTC-6): at 8:00 the month before goes on its own.
    private static readonly DateTimeOffset NovemberFirstAtEight = new(2026, 11, 1, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Accountant_ShouldBeForOwnersAndAdministrators_AndTheMonthlyOne_ForTheInvoicingWorker()
    {
        foreach (var request in new[] { typeof(SetAccountantCommand), typeof(SendIssuedCfdisToAccountantCommand), typeof(GetCfdiMonthEmailsQuery) })
            Assert.True(request.IsDefined(typeof(RequiresTenantManagerAttribute), false), request.Name);
        Assert.True(typeof(QueueMonthlyAccountantEmailCommand).IsDefined(typeof(AllowAnonymousRequestAttribute), false));
        Assert.True(typeof(ITransactionalRequest).IsAssignableFrom(typeof(SendIssuedCfdisToAccountantCommand)));
    }

    [Theory]
    [InlineData(2021, 12, Accountant, IssuedCfdis.MonthMessage)]
    [InlineData(2026, 13, Accountant, IssuedCfdis.MonthMessage)]
    [InlineData(2026, 10, " ", CfdiMonthEmail.MissingEmailMessage)]
    [InlineData(2026, 10, "Contador <contador@despacho.mx>", InvoiceEmailAddress.InvalidMessage)]
    [InlineData(2026, 10, "contador@despacho.mx, otro@despacho.mx", InvoiceEmailAddress.InvalidMessage)]
    public void Send_ShouldAskForAMonthFrom2022_AndOnePlainAddress(int year, int month, string email, string message)
    {
        var errors = new SendIssuedCfdisToAccountantCommandValidator().Validate(new SendIssuedCfdisToAccountantCommand(year, month, email)).Errors;

        Assert.Contains(message, errors.Select(e => e.ErrorMessage));
        Assert.True(new SendIssuedCfdisToAccountantCommandValidator().Validate(new SendIssuedCfdisToAccountantCommand(2026, 10, Accountant)).IsValid);
        Assert.Contains(IssuedCfdis.MonthMessage,
            new GetCfdiMonthEmailsQueryValidator().Validate(new GetCfdiMonthEmailsQuery(year == 2026 && month == 10 ? 2021 : year, month)).Errors.Select(e => e.ErrorMessage));
    }

    [Fact]
    public void SetAccountant_ShouldNeedTheEmail_ToSendEachMonth()
    {
        var validator = new SetAccountantCommandValidator();

        Assert.Equal([InvoicingProfile.MonthlyNeedsAccountantMessage],
            validator.Validate(new SetAccountantCommand(" ", Monthly: true)).Errors.Select(e => e.ErrorMessage));
        Assert.Equal([InvoiceEmailAddress.InvalidMessage],
            validator.Validate(new SetAccountantCommand("Contador <contador@despacho.mx>", Monthly: false)).Errors.Select(e => e.ErrorMessage));
        Assert.True(validator.Validate(new SetAccountantCommand(null, Monthly: false)).IsValid);
        Assert.True(validator.Validate(new SetAccountantCommand(Accountant, Monthly: true)).IsValid);
    }

    [Fact]
    public async Task SetAccountant_ShouldKeepTheEmailAndTheMonthly_AndAuditWhatChanged_WithoutTheAddress()
    {
        await using var book = await Book.OpenAsync();

        var set = await book.SetAccountantAsync($" {Accountant} ", monthly: true);

        Assert.True(set.IsSuccess, set.Error);
        Assert.Equal((Accountant, true), (set.Value.AccountantEmail, set.Value.SendsMonthToAccountant));
        Assert.Equal((Accountant, true), ((await book.SettingsAsync()).AccountantEmail, (await book.SettingsAsync()).SendsMonthToAccountant));
        // The same again changes nothing, and is not audited again.
        Assert.True((await book.SetAccountantAsync(Accountant, monthly: true)).IsSuccess);
        // Without the email nothing goes on its own: the domain refuses it even past the validator.
        Assert.Equal(InvoicingProfile.MonthlyNeedsAccountantMessage, (await book.SetAccountantAsync(null, monthly: true)).Error);
        var cleared = await book.SetAccountantAsync(null, monthly: false);
        Assert.Equal(((string?)null, false), (cleared.Value.AccountantEmail, cleared.Value.SendsMonthToAccountant));

        var audited = await book.Context.AuditLogs.AsNoTracking().Where(a => a.Action == "AccountantChanged").ToListAsync();
        Assert.Equal(2, audited.Count);
        Assert.All(audited, a => Assert.DoesNotContain("despacho", a.Metadata));
    }

    [Fact]
    public async Task Send_ShouldQueueTheMonth_ToThatAddress_AndKeepItAsTheAccountants_LeavingTheMonthlyAsItWas()
    {
        await using var book = await Book.OpenAsync();
        await book.InvoicedSaleAsync();

        var sent = await book.SendAsync(2026, 10, $" {Accountant} ");

        Assert.True(sent.IsSuccess, sent.Error);
        Assert.Equal((2026, 10, Accountant, false, (DateTimeOffset?)null, book.Clock.UtcNow),
            (sent.Value.Year, sent.Value.Month, sent.Value.To, sent.Value.ByTienditApp, sent.Value.SentAt, sent.Value.RequestedAt));
        var email = await book.Context.CfdiMonthEmails.SingleAsync();
        Assert.Equal((Guid.Parse(book.Fixture.CurrentUser.UserId!), true), (email.RequestedByUserId, email.IsPending));
        var settings = await book.SettingsAsync();
        Assert.Equal((Accountant, false), (settings.AccountantEmail, settings.SendsMonthToAccountant));
    }

    [Fact]
    public async Task Send_ShouldRefuse_AMonthThatHasNotStarted_OrOneWithoutCfdi()
    {
        await using var book = await Book.OpenAsync();
        await book.InvoicedSaleAsync();

        // It is the 4th of October: November has not started where the business is.
        Assert.Equal(AccountantCfdiEmails.NotStartedMessage, (await book.SendAsync(2026, 11, Accountant)).Error);
        Assert.Equal(AccountantCfdiEmails.NoCfdiMessage, (await book.SendAsync(2026, 9, Accountant)).Error);
        Assert.Empty(await book.Context.CfdiMonthEmails.ToListAsync());
        Assert.Null((await book.SettingsAsync()).AccountantEmail);
    }

    [Fact]
    public async Task Send_ShouldTakeFiveADay_OfTheBusiness_ByHand()
    {
        await using var book = await Book.OpenAsync();
        await book.InvoicedSaleAsync();
        // Another business's emails, and the one TienditApp sent on its own, do not count.
        await book.AddEmailsOfAnotherBusinessAsync(AccountantCfdiEmails.MaxPerBusinessPerDay);
        book.Context.CfdiMonthEmails.Add(CfdiMonthEmail.Request(book.Tenant.Id, 2026, 9, Accountant, null, book.Clock.UtcNow));
        await book.Context.SaveChangesAsync();

        for (var i = 0; i < AccountantCfdiEmails.MaxPerBusinessPerDay; i++)
            Assert.True((await book.SendAsync(2026, 10, Accountant)).IsSuccess);
        Assert.Equal(AccountantCfdiEmails.TooManyTodayMessage, (await book.SendAsync(2026, 10, Accountant)).Error);

        book.Clock.UtcNow = book.Clock.UtcNow.AddDays(1);
        Assert.True((await book.SendAsync(2026, 10, Accountant)).IsSuccess);
    }

    [Fact]
    public async Task Latest_ShouldBeTheMonthsLastFive_NewestFirst_OfTheBusinessOnly()
    {
        await using var book = await Book.OpenAsync();
        await book.AddEmailsOfAnotherBusinessAsync(1);
        var ids = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            book.Clock.UtcNow = book.Clock.UtcNow.AddMinutes(1);
            var email = CfdiMonthEmail.Request(book.Tenant.Id, 2026, 10, Accountant, i == 0 ? null : Guid.NewGuid(), book.Clock.UtcNow);
            book.Context.CfdiMonthEmails.Add(email);
            ids.Add(email.Id);
        }
        book.Context.CfdiMonthEmails.Add(CfdiMonthEmail.Request(book.Tenant.Id, 2026, 9, Accountant, null, book.Clock.UtcNow));
        await book.Context.SaveChangesAsync();

        var latest = await book.LatestAsync(2026, 10);

        Assert.Equal(Enumerable.Reverse(ids).Take(AccountantCfdiEmails.Shown), latest.Select(e => e.Id));
        Assert.Empty(await book.LatestAsync(2026, 8));
    }

    [Fact]
    public async Task Email_ShouldCarryTheSameFileAsTheDownload_WithHowManyOfEachKind_FromTheBusinessThroughTienditApp()
    {
        await using var book = await Book.OpenAsync();
        var paid = await book.InvoicedSaleAsync();
        await book.RefundAsync(paid.Id, 20m);
        Assert.Equal(1, (await book.IssuePendingCreditNotesAsync()).Value);
        var queued = (await book.SendAsync(2026, 10, Accountant)).Value;

        await book.DeliverAsync(queued.Id);

        var message = Assert.Single(book.Email.Documents);
        Assert.Equal((Accountant, "CFDI de octubre de 2026 de Negocio demo", "Negocio demo vía TienditApp", book.Tenant.ContactEmail),
            (message.To, message.Subject, message.SenderName, message.ReplyTo));
        // It is the 4th of October where the business is: the month goes up to today, and says so.
        Assert.Contains("del 1 al 4 de octubre de 2026", message.HtmlBody);
        Assert.Contains("El mes todavía no termina", message.HtmlBody);
        Assert.Contains("<li>Facturas: 1 vigente.</li>", message.HtmlBody);
        Assert.Contains("<li>Notas de crédito: 1 vigente.</li>", message.HtmlBody);
        var attachment = Assert.Single(message.Attachments);
        Assert.Equal(("CFDI_2026-10.zip", "application/zip"), (attachment.FileName, attachment.ContentType));
        using var downloaded = new ZipArchive((await book.ExportAsync()).Value.Content, ZipArchiveMode.Read);
        using var attached = new ZipArchive(new MemoryStream(attachment.Content), ZipArchiveMode.Read);
        Assert.Equal(downloaded.Entries.Select(e => e.FullName), attached.Entries.Select(e => e.FullName));

        var email = await book.Context.CfdiMonthEmails.AsNoTracking().SingleAsync();
        Assert.Equal(((int?)2, (CfdiMonthEmailFailure?)null, false), (email.CfdiCount, email.Failure, email.IsPending));
        // The outbox running it again sends nothing more.
        await book.DeliverAsync(queued.Id);
        Assert.Single(book.Email.Documents);
    }

    [Fact]
    public void Email_ShouldSayWhatWasCancelled_AndWhatHasNoStamp()
    {
        var file = new IssuedCfdiFile(
            new PrivateFileDownload(new MemoryStream(), "application/zip", "CFDI_2026-10.zip"),
            8,
            [
                new IssuedCfdiCount(IssuedCfdiKind.OrderInvoice, 3, 1, 1),
                new IssuedCfdiCount(IssuedCfdiKind.PaymentComplement, 0, 2, 0),
                new IssuedCfdiCount(IssuedCfdiKind.GlobalInvoice, 1, 0, 0)
            ],
            new IssuedCfdiMonth(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31),
                new DateTimeOffset(2026, 10, 1, 6, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero),
                TenantDateTimeResolver.Resolve("America/Mexico_City")));

        var body = AccountantCfdiEmails.Body(file, 2026, 10, "Café <Luna>\n& Co", NovemberFirstAtEight);

        Assert.Contains("<li>Facturas: 3 vigentes, 1 cancelada, 1 sin timbre (solo en la lista).</li>", body);
        Assert.Contains("<li>Complementos de pago: 0 vigentes, 2 cancelados.</li>", body);
        Assert.Contains("<li>Facturas globales: 1 vigente.</li>", body);
        Assert.Contains("Caf&#233; &lt;Luna&gt; &amp; Co te envía", body);
        // A month that ended goes whole.
        Assert.Contains("del 1 al 31 de octubre de 2026", body);
        Assert.DoesNotContain("todavía no termina", body);
        Assert.Equal("CFDI de octubre de 2026 de Café <Luna> & Co", AccountantCfdiEmails.Subject(2026, 10, "Café <Luna>\n& Co"));
    }

    [Fact]
    public async Task Email_ShouldEnd_WhenTheMonthHasMoreCfdiThanItTakes_OrItsFileIsTooBigForAnEmail()
    {
        await using var book = await Book.OpenAsync();
        await book.InvoicedSaleAsync();
        await book.InvoicedSaleAsync();
        var tooMany = (await book.SendAsync(2026, 10, Accountant)).Value;
        var tooBig = (await book.SendAsync(2026, 10, Accountant)).Value;
        var exactly = (await book.SendAsync(2026, 10, Accountant)).Value;

        await book.DeliverAsync(tooMany.Id, limit: 1);
        await book.DeliverAsync(tooBig.Id, maxFileBytes: 100);
        // As many CFDI as it takes: all of them go.
        await book.DeliverAsync(exactly.Id, limit: 2);

        var emails = await book.Context.CfdiMonthEmails.AsNoTracking().ToDictionaryAsync(e => e.Id);
        Assert.Equal(CfdiMonthEmailFailure.TooMany, emails[tooMany.Id].Failure);
        Assert.Equal(CfdiMonthEmailFailure.TooLarge, emails[tooBig.Id].Failure);
        Assert.Equal((int?)2, emails[exactly.Id].CfdiCount);
        Assert.Single(book.Email.Documents);
    }

    [Fact]
    public async Task Email_ShouldBeTriedAgain_WhileTheServerDoesNotTakeIt_AndEndOnTheLastAttempt_OrWhenRefusedForGood()
    {
        await using var book = await Book.OpenAsync();
        await book.InvoicedSaleAsync();
        var retried = (await book.SendAsync(2026, 10, Accountant)).Value;
        var rejected = (await book.SendAsync(2026, 10, Accountant)).Value;
        var unconfigured = (await book.SendAsync(2026, 10, Accountant)).Value;

        book.Email.DocumentsFailure = new InvalidOperationException("SMTP 421");
        await Assert.ThrowsAsync<InvalidOperationException>(() => book.DeliverAsync(retried.Id));
        var pending = await book.Context.CfdiMonthEmails.AsNoTracking().SingleAsync(e => e.Id == retried.Id);
        Assert.Equal((1, true), (pending.FailedAttempts, pending.IsPending));
        // The outbox's last attempt ends it, so it never stays pending.
        await book.DeliverAsync(retried.Id, attempt: 8, maxAttempts: 8);
        book.Email.DocumentsFailure = null;
        book.Email.DocumentsOutcome = EmailSendOutcome.Rejected;
        await book.DeliverAsync(rejected.Id);
        book.Email.DocumentsOutcome = EmailSendOutcome.NotConfigured;
        await book.DeliverAsync(unconfigured.Id);

        var emails = await book.Context.CfdiMonthEmails.AsNoTracking().ToDictionaryAsync(e => e.Id);
        Assert.Equal((CfdiMonthEmailFailure?)CfdiMonthEmailFailure.NotDelivered, emails[retried.Id].Failure);
        Assert.Equal((CfdiMonthEmailFailure?)CfdiMonthEmailFailure.Rejected, emails[rejected.Id].Failure);
        Assert.Equal((CfdiMonthEmailFailure?)CfdiMonthEmailFailure.NotConfigured, emails[unconfigured.Id].Failure);
        Assert.Empty(book.Email.Documents);
    }

    [Fact]
    public async Task Monthly_ShouldQueueTheMonthBefore_FromTheFirstDayAtEight_ToDaySeven_WhereTheBusinessIs_Once()
    {
        await using var book = await Book.OpenAsync();
        await book.InvoicedSaleAsync();
        Assert.True((await book.SetAccountantAsync(Accountant, monthly: true)).IsSuccess);

        book.Clock.UtcNow = NovemberFirstAtEight.AddMinutes(-1);
        Assert.Equal(0, await book.QueueMonthlyAsync());
        book.Clock.UtcNow = NovemberFirstAtEight;
        Assert.Equal(1, await book.QueueMonthlyAsync());
        // Once: the next passes of the worker queue nothing more.
        book.Clock.UtcNow = NovemberFirstAtEight.AddDays(3);
        Assert.Equal(0, await book.QueueMonthlyAsync());

        var email = await book.Context.CfdiMonthEmails.AsNoTracking().SingleAsync();
        Assert.Equal((2026, 10, Accountant, (Guid?)null, NovemberFirstAtEight),
            (email.Year, email.Month, email.To, email.RequestedByUserId, email.RequestedAt));
    }

    [Fact]
    public async Task Monthly_ShouldWaitForDaySevenToEnd_AndGoByTheBusinessesTimeZone()
    {
        await using var book = await Book.OpenAsync();
        await book.InvoicedSaleAsync();
        Assert.True((await book.SetAccountantAsync(Accountant, monthly: true)).IsSuccess);

        // Midnight of the 8th in Mexico City: too late, it waits for a person.
        book.Clock.UtcNow = new DateTimeOffset(2026, 11, 8, 6, 0, 0, TimeSpan.Zero);
        Assert.Equal(0, await book.QueueMonthlyAsync());
        // In Cancún (UTC-5) it is 8:00 of the first an hour before Mexico City.
        book.Clock.UtcNow = NovemberFirstAtEight.AddHours(-1);
        Assert.Equal(0, await book.QueueMonthlyAsync());
        (await book.Context.TenantSettings.SingleAsync()).SetTimeZone("America/Cancun");
        await book.Context.SaveChangesAsync();
        Assert.Equal(1, await book.QueueMonthlyAsync());
    }

    [Fact]
    public async Task Monthly_ShouldQueueNothing_WithoutTheMonthlyOrCfdiInTheMonthBefore()
    {
        await using var book = await Book.OpenAsync();
        book.Clock.UtcNow = NovemberFirstAtEight;
        Assert.Equal(0, await book.QueueMonthlyAsync());

        // The email alone does not send it each month.
        Assert.True((await book.SetAccountantAsync(Accountant, monthly: false)).IsSuccess);
        book.Clock.UtcNow = SalesTestFixture.Now;
        await book.InvoicedSaleAsync();
        book.Clock.UtcNow = NovemberFirstAtEight;
        Assert.Equal(0, await book.QueueMonthlyAsync());

        // With the monthly on, a month without CFDI (November, at the start of December) sends nothing.
        Assert.True((await book.SetAccountantAsync(Accountant, monthly: true)).IsSuccess);
        book.Clock.UtcNow = NovemberFirstAtEight.AddMonths(1);
        Assert.Equal(0, await book.QueueMonthlyAsync());
        Assert.Empty(await book.Context.CfdiMonthEmails.ToListAsync());
    }

    [Fact]
    public void MonthlyDays_ShouldCoverEveryTimeZone()
    {
        Assert.True(AccountantCfdiEmails.InMonthlyDaysSomewhere(new DateTimeOffset(2026, 10, 31, 20, 0, 0, TimeSpan.Zero)));
        Assert.True(AccountantCfdiEmails.InMonthlyDaysSomewhere(new DateTimeOffset(2026, 11, 8, 11, 0, 0, TimeSpan.Zero)));
        Assert.False(AccountantCfdiEmails.InMonthlyDaysSomewhere(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero)));
        Assert.False(AccountantCfdiEmails.InMonthlyDaysSomewhere(new DateTimeOffset(2026, 10, 30, 23, 0, 0, TimeSpan.Zero)));
        // January sends December of the year before.
        Assert.Equal((2026, 12), AccountantCfdiEmails.MonthlyMonth(new DateTimeOffset(2027, 1, 2, 18, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc));
        // Nothing before CFDI 4.0.
        Assert.Null(AccountantCfdiEmails.MonthlyMonth(new DateTimeOffset(2022, 1, 2, 18, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc));
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
        public RecordingEmailService Email { get; } = new();
        public SalesDbContext Context => Fixture.Context;
        public SalesClock Clock => Fixture.Clock;

        public static async Task<Book> OpenAsync()
        {
            var fixture = SalesTestFixture.Create();
            var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, pricesIncludeTax: true);
            await fixture.OpenCashSessionAsync(tenant);
            tenant.SetBusinessInfo(
                Rfc, "ESCUELA KEMPER URGATE", "Comercio", "601 - General de Ley Personas Morales",
                new Address("Reforma", "1", null, "Centro", "06000", "Cuauhtémoc", "Ciudad de México"));
            await fixture.Context.SaveChangesAsync();
            var book = new Book(fixture, tenant, await fixture.AddProductAsync(tenant, "Café", 100m));
            var (cer, key) = TestCsd.Make(uniqueIdentifier: $"{Rfc} / VADA800927HSRSRL05");
            var uploaded = await new UploadInvoicingCertificateCommandHandler(
                    book.Context, fixture.TenantService, fixture.CurrentUser, book.Gateway, book.Clock,
                    NullLogger<UploadInvoicingCertificateCommandHandler>.Instance)
                .Handle(new UploadInvoicingCertificateCommand(Convert.ToBase64String(cer), Convert.ToBase64String(key), TestCsd.Password),
                    CancellationToken.None);
            Assert.True(uploaded.IsSuccess, uploaded.Error);
            return book;
        }

        /// <summary>A $100 sale to Karla, paid by card and invoiced; the clock moves a minute first, so nothing shares its time.</summary>
        public async Task<OrderDto> InvoicedSaleAsync()
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
            Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
            var issued = await new IssueOrderInvoiceCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssueOrderInvoiceCommand(created.Value.Id, Receiver, false), CancellationToken.None);
            Assert.True(issued.IsSuccess, issued.Error);
            Context.ChangeTracker.Clear();
            return created.Value;
        }

        public async Task RefundAsync(Guid orderId, decimal amount)
        {
            Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
            var order = await Context.Orders.Include(o => o.Payments).Include(o => o.Refunds).Include(o => o.Items).SingleAsync(o => o.Id == orderId);
            order.RegisterRefund(amount, OrderPaymentMethod.Card, "Devolución de un café", Clock.UtcNow, "cajero", null, null, false, []);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        public Task<Result<int>> IssuePendingCreditNotesAsync()
            => new IssuePendingCreditNotesCommandHandler(this, Context, Gateway, Storage, Clock, NullLogger<OrderInvoiceStamper>.Instance)
                .Handle(new IssuePendingCreditNotesCommand(), CancellationToken.None);

        /// <summary>Emails of the month of another business, asked for by hand just now.</summary>
        public async Task AddEmailsOfAnotherBusinessAsync(int count)
        {
            var other = new Tenant("Negocio otro", "otro", BusinessType.Retail, "otro@demo.com");
            Context.Tenants.Add(other);
            for (var i = 0; i < count; i++)
                Context.CfdiMonthEmails.Add(CfdiMonthEmail.Request(other.Id, 2026, 10, "otro@despacho.mx", Guid.NewGuid(), Clock.UtcNow));
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        public async Task<Result<InvoicingSettingsDto>> SetAccountantAsync(string? email, bool monthly)
        {
            var set = await new SetAccountantCommandHandler(Context, Fixture.TenantService, Fixture.CurrentUser, Gateway, Clock)
                .Handle(new SetAccountantCommand(email, monthly), CancellationToken.None);
            Context.ChangeTracker.Clear();
            return set;
        }

        public Task<InvoicingSettingsDto> SettingsAsync()
            => new GetInvoicingSettingsQueryHandler(Context, Fixture.TenantService, Gateway, Clock)
                .Handle(new GetInvoicingSettingsQuery(), CancellationToken.None);

        public async Task<Result<CfdiMonthEmailDto>> SendAsync(int year, int month, string email)
        {
            var sent = await new SendIssuedCfdisToAccountantCommandHandler(
                    Context, Fixture.TenantService, Fixture.CurrentUser, new InvoicingLock(Context), Clock)
                .Handle(new SendIssuedCfdisToAccountantCommand(year, month, email), CancellationToken.None);
            Context.ChangeTracker.Clear();
            return sent;
        }

        public Task<List<CfdiMonthEmailDto>> LatestAsync(int year, int month)
            => new GetCfdiMonthEmailsQueryHandler(Context).Handle(new GetCfdiMonthEmailsQuery(year, month), CancellationToken.None);

        public Task<Result<PrivateFileDownload>> ExportAsync()
            => new ExportIssuedCfdisQueryHandler(Context, Fixture.TenantService, Storage, NullLogger<ExportIssuedCfdisQueryHandler>.Instance)
                .Handle(new ExportIssuedCfdisQuery(2026, 10), CancellationToken.None);

        /// <summary>The invoicing worker, in the business's scope.</summary>
        public async Task<int> QueueMonthlyAsync()
        {
            var queued = await new QueueMonthlyAccountantEmailCommandHandler(Context, Fixture.TenantService, Clock)
                .Handle(new QueueMonthlyAccountantEmailCommand(), CancellationToken.None);
            Context.ChangeTracker.Clear();
            return queued.Value;
        }

        /// <summary>The outbox runs the email's handler in its own scope, and saves what it changed when it ends well.</summary>
        public async Task DeliverAsync(
            Guid emailId, int attempt = 1, int maxAttempts = 8, int limit = IssuedCfdiFiles.MaxCount,
            int maxFileBytes = AccountantCfdiEmails.MaxFileBytes)
        {
            Context.ChangeTracker.Clear();
            var handler = new CfdiMonthEmailRequestedHandler(Context, Storage, Email, Clock, NullLogger<CfdiMonthEmailRequestedHandler>.Instance)
            {
                Limit = limit,
                MaxFileBytes = maxFileBytes
            };
            try
            {
                await handler.HandleAsync(
                    new CfdiMonthEmailRequestedEvent(emailId),
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
