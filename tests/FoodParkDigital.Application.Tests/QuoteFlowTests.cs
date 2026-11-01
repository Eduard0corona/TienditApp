using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Appointments.Commands;
using FoodParkDigital.Application.Appointments.DTOs;
using FoodParkDigital.Application.Appointments.HomeVisits;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Inventory;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.DTOs;
using FoodParkDigital.Application.Quotes;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The quotes through their handlers (Fase 29C, ADR 0053): a draft for a customer or for a visit, totalled by the server, sent
/// with the business's validity and a link, corrected with versions, answered by the customer, the visit taken off while its
/// sale stands, the link without an account, and every business with its own quotes.
/// </summary>
public class QuoteFlowTests
{
    // SalesTestFixture.Now is Sunday 4 October 2026, 11:00 in Mexico City: the business's day is 4 October.
    private static readonly DateOnly Today = new(2026, 10, 4);

    [Fact]
    public async Task Create_ShouldStartADraft_WithAFolio_TheBusinessValidity_AndItsPrices()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture, pricesIncludeTax: true);
        (await fixture.Context.TenantSettings.SingleAsync()).SetQuoteValidityDays(20);
        await fixture.Context.SaveChangesAsync();

        var first = await CreateAsync(fixture);
        var second = await CreateAsync(fixture);

        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal(("COT-000001", 1, QuoteStatus.Draft, 20, true),
            (first.Value!.Number, first.Value.Revision, first.Value.Status, first.Value.ValidityDays, first.Value.PricesIncludeTax));
        Assert.Equal(("Valeria Ruiz", "5522107765", "valeria@correo.mx"),
            (first.Value.CustomerName, first.Value.CustomerPhone, first.Value.CustomerEmail));
        Assert.Equal("COT-000002", second.Value!.Number);
        Assert.Equal(2, await fixture.Context.AuditLogs.CountAsync(a => a.Action == "QuoteCreated" && a.TenantId == tenant.Id));
    }

    [Fact]
    public async Task Create_ShouldNeedACustomerOfTheBusiness_WhenItNamesOne()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await TradeAsync(fixture, slug: "otro");
        var foreign = new Customer(other.Id, "Ana", "5512345678", null);
        fixture.Context.Customers.Add(foreign);
        await fixture.Context.SaveChangesAsync();
        await TradeAsync(fixture);

        var refused = await CreateAsync(fixture, new CreateQuoteCommand(null, foreign.Id, "Ana", "5512345678", null));

        Assert.Equal(QuotesHandler.CustomerNotFoundMessage, refused.Error);
        Assert.Equal(0, await fixture.Context.Quotes.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Create_FromAVisit_ShouldTakeItsCustomer_AndOpenTheSameQuoteAfterwards()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var visit = await VisitAsync(fixture);

        var created = await CreateAsync(fixture, new CreateQuoteCommand(visit.Id, null, null, null, null));
        var again = await CreateAsync(fixture, new CreateQuoteCommand(visit.Id, null, null, null, null));

        Assert.True(created.IsSuccess, created.Error);
        Assert.Equal(("Luis", "5512345678", visit.Id, "72000"),
            (created.Value!.CustomerName, created.Value.CustomerPhone, created.Value.Appointment!.Id, created.Value.Appointment.PostalCode));
        Assert.Equal(created.Value.Id, again.Value!.Id);
        Assert.Equal(1, await fixture.Context.Quotes.CountAsync());
    }

    [Fact]
    public async Task Draft_ShouldBeTotalledByTheServer_WithTheProductsIva_AndTheBusinessesForALineByHand()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture, pricesIncludeTax: false);
        var heater = await fixture.AddProductAsync(tenant, "Cambio de calentador", 1000m, unit: UnitType.Service);
        var pipe = await fixture.AddProductAsync(tenant, "Tubo de cobre", 50m, isTaxExempt: true);
        var quote = (await CreateAsync(fixture)).Value!;

        var saved = await UpdateAsync(fixture, quote, [
            new QuoteLineRequest { ProductId = heater.Id, Quantity = 1m, UnitPrice = 900m },
            new QuoteLineRequest { ProductId = pipe.Id, Description = "Tubo de cobre de 1/2", Quantity = 3m, UnitPrice = 50m },
            new QuoteLineRequest { Description = "Mano de obra", Quantity = 2.5m, UnitPrice = 200m }]);

        Assert.True(saved.IsSuccess, saved.Error);
        var lines = saved.Value!.Lines;
        Assert.Equal([("Cambio de calentador", 900m, 16m, false), ("Tubo de cobre de 1/2", 150m, 0m, true), ("Mano de obra", 500m, 16m, false)],
            lines.Select(l => (l.Description, l.Total, l.TaxRate, l.IsTaxExempt)));
        // 1,400 at 16 % adds 224; the exempt 150 adds nothing.
        Assert.Equal((1550m, 224m, 1774m, 1774m), (saved.Value.SubTotal, saved.Value.Tax, saved.Value.Total, saved.Value.ToPay));
        Assert.Equal([(16m, false, 1400m, 224m), (0m, true, 150m, 0m)],
            saved.Value.Taxes.Select(t => (t.RatePercent, t.IsExempt, t.TaxableBase, t.TaxAmount)));
    }

    [Fact]
    public async Task Draft_ShouldRefuseAProductOfAnotherBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var other = await TradeAsync(fixture, slug: "otro");
        var foreign = await fixture.AddProductAsync(other, "Ajeno", 100m);
        await TradeAsync(fixture);
        var quote = (await CreateAsync(fixture)).Value!;

        var refused = await UpdateAsync(fixture, quote, [new QuoteLineRequest { ProductId = foreign.Id, Quantity = 1m, UnitPrice = 100m }]);

        Assert.Equal(QuotesHandler.ProductNotFoundMessage, refused.Error);
    }

    [Fact]
    public async Task Send_ShouldBeValidFromTheBusinessesDay_WithALink_AndThenNotChange()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var quote = await DraftWithALineAsync(fixture);

        var sent = await Handler(fixture).Handle(new SendQuoteCommand(quote.Id), CancellationToken.None);

        Assert.True(sent.IsSuccess, sent.Error);
        Assert.Equal((QuoteStatus.Sent, new DateOnly(2026, 10, 19)), (sent.Value!.Status, sent.Value.ValidUntil!.Value));
        Assert.Equal(43, sent.Value.TrackingToken!.Length);
        Assert.Equal(1, await fixture.Context.AuditLogs.CountAsync(a => a.Action == "QuoteSent"));
        var edit = await UpdateAsync(fixture, sent.Value, [new QuoteLineRequest { Description = "Otra", Quantity = 1m, UnitPrice = 1m }]);
        Assert.Equal(QuoteRules.NotDraftMessage, edit.Error);
    }

    [Fact]
    public async Task Send_ShouldNeedALineWithAPrice()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var quote = (await CreateAsync(fixture)).Value!;

        var refused = await Handler(fixture).Handle(new SendQuoteCommand(quote.Id), CancellationToken.None);

        Assert.Equal(QuoteRules.NothingToSendMessage, refused.Error);
    }

    [Fact]
    public async Task ACorrection_ShouldReplaceTheVersionTheCustomerHad_WhenSent_AndOpenAgainWhileItIsADraft()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var sent = await SentAsync(fixture);

        var correction = await Handler(fixture).Handle(new ReviseQuoteCommand(sent.Id), CancellationToken.None);
        var again = await Handler(fixture).Handle(new ReviseQuoteCommand(sent.Id), CancellationToken.None);

        Assert.True(correction.IsSuccess, correction.Error);
        Assert.Equal((sent.Number, 2, QuoteStatus.Draft), (correction.Value!.Number, correction.Value.Revision, correction.Value.Status));
        Assert.Equal(correction.Value.Id, again.Value!.Id);
        Assert.Equal(correction.Value.Id, (await GetAsync(fixture, sent.Id)).DraftRevisionId);

        await UpdateAsync(fixture, correction.Value, [new QuoteLineRequest { Description = "Cambio de calentador", Quantity = 1m, UnitPrice = 900m }]);
        var resent = await Handler(fixture).Handle(new SendQuoteCommand(correction.Value.Id), CancellationToken.None);

        Assert.True(resent.IsSuccess, resent.Error);
        var first = await GetAsync(fixture, sent.Id);
        Assert.Equal((QuoteStatus.Superseded, 1160m), (first.Status, first.Total));
        Assert.Equal([(1, QuoteStatus.Superseded), (2, QuoteStatus.Sent)], resent.Value!.Versions.Select(v => (v.Revision, v.Status)));
        Assert.NotEqual(sent.TrackingToken, resent.Value.TrackingToken);
    }

    [Fact]
    public async Task ACorrection_ShouldComeAfterADiscardedOne()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var sent = await SentAsync(fixture);
        var dropped = (await Handler(fixture).Handle(new ReviseQuoteCommand(sent.Id), CancellationToken.None)).Value!;
        var discarded = await Handler(fixture).Handle(new DiscardQuoteCommand(dropped.Id), CancellationToken.None);

        var next = await Handler(fixture).Handle(new ReviseQuoteCommand(sent.Id), CancellationToken.None);

        Assert.Equal(QuoteStatus.Discarded, discarded.Value!.Status);
        Assert.Equal(3, next.Value!.Revision);
        Assert.Equal(QuoteRules.DraftOnlyDiscardMessage,
            (await Handler(fixture).Handle(new DiscardQuoteCommand(sent.Id), CancellationToken.None)).Error);
    }

    [Fact]
    public async Task Reply_ShouldKeepWhatTheCustomerSaid_OnTheQuote_AndOnlyThatTheyAnsweredInTheActivity()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var sent = await SentAsync(fixture);

        var accepted = await Handler(fixture).Handle(
            new RecordQuoteReplyCommand(sent.Id, true, QuoteReplyChannel.WhatsApp, "Sí, el jueves temprano"), CancellationToken.None);

        Assert.True(accepted.IsSuccess, accepted.Error);
        Assert.Equal((QuoteStatus.Accepted, QuoteReplyChannel.WhatsApp, "Sí, el jueves temprano"),
            (accepted.Value!.Status, accepted.Value.ReplyChannel!.Value, accepted.Value.CustomerResponse));
        var entry = await fixture.Context.AuditLogs.SingleAsync(a => a.Action == "QuoteAccepted");
        Assert.Contains("WhatsApp", entry.Metadata);
        Assert.DoesNotContain("jueves", entry.Metadata);
        Assert.Equal(QuoteRules.CannotReviseMessage,
            (await Handler(fixture).Handle(new ReviseQuoteCommand(sent.Id), CancellationToken.None)).Error);
    }

    [Fact]
    public async Task AnExpiredQuote_ShouldNotBeAnswered_ButCorrected()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var sent = await SentAsync(fixture);
        // 20 October, the day after its validity, in Mexico City.
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(16);

        var refused = await Handler(fixture).Handle(
            new RecordQuoteReplyCommand(sent.Id, false, QuoteReplyChannel.Phone, null), CancellationToken.None);
        var corrected = await Handler(fixture).Handle(new ReviseQuoteCommand(sent.Id), CancellationToken.None);

        Assert.Equal(QuoteRules.ExpiredMessage, refused.Error);
        Assert.Equal(QuoteStatus.Expired, (await GetAsync(fixture, sent.Id)).Status);
        Assert.True(corrected.IsSuccess, corrected.Error);
    }

    [Fact]
    public async Task List_ShouldFilterByWhereTheyAre_AndSearch_LeavingTheReplacedInTheirHistory()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var draft = (await CreateAsync(fixture, new CreateQuoteCommand(null, null, "Ana López", "5511112222", null))).Value!;
        var replaced = await SentAsync(fixture);
        var correction = (await Handler(fixture).Handle(new ReviseQuoteCommand(replaced.Id), CancellationToken.None)).Value!;
        await Handler(fixture).Handle(new SendQuoteCommand(correction.Id), CancellationToken.None);
        var expired = await SentAsync(fixture, validityDays: 1);
        var rejected = await SentAsync(fixture);
        await Handler(fixture).Handle(new RecordQuoteReplyCommand(rejected.Id, false, QuoteReplyChannel.Email, null), CancellationToken.None);
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(2);

        async Task<List<(string, int, QuoteStatus)>> ListAsync(QuoteListFilter filter, string? search = null)
            => (await Handler(fixture).Handle(new ListQuotesQuery(filter, search), CancellationToken.None))
                .Items.Select(q => (q.Number, q.Revision, q.Status)).OrderBy(q => q.Number).ToList();

        Assert.Equal([(draft.Number, 1, QuoteStatus.Draft), (replaced.Number, 2, QuoteStatus.Sent)], await ListAsync(QuoteListFilter.Open));
        Assert.Equal([(expired.Number, 1, QuoteStatus.Expired)], await ListAsync(QuoteListFilter.Expired));
        Assert.Equal([(rejected.Number, 1, QuoteStatus.Rejected)], await ListAsync(QuoteListFilter.Rejected));
        Assert.Equal(4, (await ListAsync(QuoteListFilter.All)).Count);
        Assert.Equal([(draft.Number, 1, QuoteStatus.Draft)], await ListAsync(QuoteListFilter.All, "Ana"));
        Assert.Equal([(draft.Number, 1, QuoteStatus.Draft)], await ListAsync(QuoteListFilter.All, "1111"));
    }

    [Fact]
    public async Task TheVisit_ShouldComeOff_WhatItsSaleCharged_WhileTheSaleIsNotCancelled()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture, pricesIncludeTax: false, policy: HomeVisitChargePolicy.CreditedIfHired);
        var service = await fixture.AddProductAsync(tenant, "Visita y diagnóstico", 300m, unit: UnitType.Service);
        var visit = await VisitAsync(fixture, service.Id);
        var sale = await SellVisitAsync(fixture, tenant, service, visit.Id);
        var quote = (await CreateAsync(fixture, new CreateQuoteCommand(visit.Id, null, null, null, null))).Value!;

        var saved = await UpdateAsync(fixture, quote, [new QuoteLineRequest { Description = "Cambio de calentador", Quantity = 1m, UnitPrice = 1000m }]);

        // The visit service, 300 plus IVA, and the $80 of the zone plus IVA: 440.80 paid.
        Assert.Equal((1160m, 440.80m, true, 719.20m),
            (saved.Value!.Total, saved.Value.VisitCredit!.Amount, saved.Value.VisitCredit.Applies, saved.Value.ToPay));

        var sent = (await Handler(fixture).Handle(new SendQuoteCommand(quote.Id), CancellationToken.None)).Value!;
        var order = await fixture.Context.Orders.SingleAsync(o => o.Id == sale.Id);
        fixture.Context.Entry(order).Property(o => o.Status).CurrentValue = OrderStatus.Cancelled;
        await fixture.Context.SaveChangesAsync();

        var after = await GetAsync(fixture, sent.Id);
        Assert.Equal((440.80m, false, 1160m), (after.VisitCredit!.Amount, after.VisitCredit.Applies, after.ToPay));
    }

    [Theory]
    [InlineData(HomeVisitChargePolicy.Charged)]
    [InlineData(HomeVisitChargePolicy.Free)]
    public async Task TheVisit_ShouldNotComeOff_WhenTheBusinessDoesNotTakeItOff(HomeVisitChargePolicy policy)
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture, policy: policy);
        var service = await fixture.AddProductAsync(tenant, "Visita y diagnóstico", 300m, unit: UnitType.Service);
        var visit = await VisitAsync(fixture, service.Id);
        await SellVisitAsync(fixture, tenant, service, visit.Id);

        var quote = (await CreateAsync(fixture, new CreateQuoteCommand(visit.Id, null, null, null, null))).Value!;

        Assert.Null(quote.VisitCredit);
    }

    [Fact]
    public async Task TheVisit_ShouldNotComeOff_BeforeItIsCharged()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture, policy: HomeVisitChargePolicy.CreditedIfHired);
        var visit = await VisitAsync(fixture);

        var quote = (await CreateAsync(fixture, new CreateQuoteCommand(visit.Id, null, null, null, null))).Value!;

        Assert.Null(quote.VisitCredit);
    }

    [Fact]
    public async Task TheLink_ShouldShowTheQuote_WithOnlyTheCustomersName_UntilThirtyDaysAfterItsValidity()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var sent = await SentAsync(fixture);
        // Another business is in context: the link carries its own.
        await TradeAsync(fixture, slug: "otro");

        var view = await PublicAsync(fixture, sent.TrackingToken!);

        Assert.NotNull(view);
        Assert.Equal(("Negocio plomero", sent.Number, QuoteStatus.Sent, "Valeria Ruiz", 1160m, new DateOnly(2026, 10, 19)),
            (view.BusinessName, view.Number, view.Status, view.CustomerName, view.Total, view.ValidUntil));
        Assert.Equal([("Cambio de calentador", 1m, 1160m)], view.Lines.Select(l => (l.Description, l.Quantity, l.Total)));
        var json = System.Text.Json.JsonSerializer.Serialize(view);
        Assert.DoesNotContain("5522107765", json);
        Assert.DoesNotContain("valeria@correo.mx", json);

        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(45);
        Assert.Equal(QuoteStatus.Expired, (await PublicAsync(fixture, sent.TrackingToken!))!.Status);
        fixture.Clock.UtcNow = SalesTestFixture.Now.AddDays(46);
        Assert.Null(await PublicAsync(fixture, sent.TrackingToken!));
        Assert.Null(await PublicAsync(fixture, sent.TrackingToken!.ToUpperInvariant()));
        Assert.Null(await PublicAsync(fixture, "corto"));
    }

    [Fact]
    public async Task TheLinkOfAReplacedVersion_ShouldSayItWasReplaced_AndLeadToTheNewOne()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var sent = await SentAsync(fixture);
        var correction = (await Handler(fixture).Handle(new ReviseQuoteCommand(sent.Id), CancellationToken.None)).Value!;
        var resent = (await Handler(fixture).Handle(new SendQuoteCommand(correction.Id), CancellationToken.None)).Value!;

        var old = await PublicAsync(fixture, sent.TrackingToken!);

        Assert.Equal((QuoteStatus.Superseded, resent.TrackingToken), (old!.Status, old.NewerToken));
        Assert.Null((await PublicAsync(fixture, resent.TrackingToken!))!.NewerToken);
    }

    [Fact]
    public async Task EveryBusiness_ShouldSeeAndChangeOnlyItsOwnQuotes()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture, slug: "otro");
        var theirs = await SentAsync(fixture);
        await TradeAsync(fixture);
        await SentAsync(fixture);

        Assert.Single((await Handler(fixture).Handle(new ListQuotesQuery(QuoteListFilter.All), CancellationToken.None)).Items);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => GetAsync(fixture, theirs.Id));
        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => Handler(fixture).Handle(new ReviseQuoteCommand(theirs.Id), CancellationToken.None));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => Handler(fixture).Handle(
            new RecordQuoteReplyCommand(theirs.Id, true, QuoteReplyChannel.WhatsApp, null), CancellationToken.None));
        // Each business counts its own folios.
        Assert.Equal("COT-000001", (await Handler(fixture).Handle(new ListQuotesQuery(QuoteListFilter.All), CancellationToken.None)).Items[0].Number);
    }

    [Fact]
    public async Task Settings_ShouldChangeHowLongQuotesAreValid_AndKeepItInTheActivity()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);

        var saved = await Handler(fixture).Handle(new UpdateQuoteSettingsCommand(30), CancellationToken.None);
        var same = await Handler(fixture).Handle(new UpdateQuoteSettingsCommand(30), CancellationToken.None);

        Assert.Equal((30, 30), (saved.Value!.ValidityDays, same.Value!.ValidityDays));
        Assert.Equal(1, await fixture.Context.AuditLogs.CountAsync(a => a.Action == "QuoteSettingsUpdated"));
        Assert.Equal(30, (await CreateAsync(fixture)).Value!.ValidityDays);
        Assert.False(new UpdateQuoteSettingsCommandValidator().Validate(new UpdateQuoteSettingsCommand(91)).IsValid);
    }

    [Fact]
    public void Validators_ShouldSayWhatIsMissing()
    {
        var create = new CreateQuoteCommandValidator().Validate(new CreateQuoteCommand(null, null, "", "", "valeria"));
        Assert.Equal([QuoteRules.CustomerNameMessage, QuoteRules.CustomerPhoneMessage, Domain.Fiscal.InvoiceEmailAddress.InvalidMessage],
            create.Errors.Select(e => e.ErrorMessage));
        // From a visit, the customer comes from it.
        Assert.True(new CreateQuoteCommandValidator().Validate(new CreateQuoteCommand(Guid.NewGuid(), null, null, null, null)).IsValid);

        var draft = new UpdateQuoteDraftCommandValidator().Validate(new UpdateQuoteDraftCommand(
            Guid.NewGuid(), null, "Valeria", "5522107765", null, 15, null,
            [new QuoteLineRequest { Quantity = 0m, UnitPrice = 10.001m }]));
        Assert.Equal([QuoteRules.DescriptionMessage, QuoteRules.QuantityMessage, QuoteRules.UnitPriceMessage],
            draft.Errors.Select(e => e.ErrorMessage));
    }

    private static QuotesHandler Handler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock);

    private static Task<Result<QuoteDto>> CreateAsync(SalesTestFixture fixture, CreateQuoteCommand? command = null)
        => new CreateQuoteCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, new FolioGenerator(fixture.Context), fixture.Clock).Handle(command ?? new CreateQuoteCommand(null, null, "Valeria Ruiz", "5522107765", "valeria@correo.mx"),
            CancellationToken.None);

    private static Task<Result<QuoteDto>> UpdateAsync(SalesTestFixture fixture, QuoteDto quote, List<QuoteLineRequest> lines, int? validityDays = null)
        => Handler(fixture).Handle(new UpdateQuoteDraftCommand(
            quote.Id, quote.CustomerId, quote.CustomerName, quote.CustomerPhone, quote.CustomerEmail, validityDays ?? quote.ValidityDays,
            quote.Note, lines), CancellationToken.None);

    private static Task<QuoteDto> GetAsync(SalesTestFixture fixture, Guid id)
        => Handler(fixture).Handle(new GetQuoteQuery(id), CancellationToken.None);

    private static Task<PublicQuoteDto?> PublicAsync(SalesTestFixture fixture, string token)
        => new GetPublicQuoteQueryHandler(fixture.Context, fixture.TenantService, fixture.Clock)
            .Handle(new GetPublicQuoteQuery(token), CancellationToken.None);

    /// <summary>A draft with «Cambio de calentador» for $1,160.</summary>
    private static async Task<QuoteDto> DraftWithALineAsync(SalesTestFixture fixture, int validityDays = 15)
    {
        var quote = (await CreateAsync(fixture)).Value!;
        return (await UpdateAsync(fixture, quote,
            [new QuoteLineRequest { Description = "Cambio de calentador", Quantity = 1m, UnitPrice = 1160m }], validityDays)).Value!;
    }

    private static async Task<QuoteDto> SentAsync(SalesTestFixture fixture, int validityDays = 15)
    {
        var draft = await DraftWithALineAsync(fixture, validityDays);
        var sent = await Handler(fixture).Handle(new SendQuoteCommand(draft.Id), CancellationToken.None);
        Assert.True(sent.IsSuccess, sent.Error);
        return sent.Value!;
    }

    /// <summary>A trade with the agenda and the zone «Centro» (72000, $80), its prices with IVA inside unless said.</summary>
    private static async Task<Domain.Tenants.Entities.Tenant> TradeAsync(
        SalesTestFixture fixture, bool pricesIncludeTax = true, string slug = "plomero",
        HomeVisitChargePolicy policy = HomeVisitChargePolicy.Charged)
    {
        var tenant = await fixture.AddTenantAsync(
            BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments, pricesIncludeTax: pricesIncludeTax, slug: slug);
        (await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id)).UpdateHomeVisitRequests(true, null, policy);
        fixture.Context.DeliveryZones.Add(new DeliveryZone(tenant.Id, new DeliveryZoneFields("Centro", 80m, 0m, null, ["72000"])));
        await fixture.Context.SaveChangesAsync();
        return tenant;
    }

    /// <summary>A visit the team scheduled at 72000 tomorrow at 10, with the service when given.</summary>
    private static async Task<AppointmentDto> VisitAsync(SalesTestFixture fixture, Guid? serviceId = null)
    {
        var start = SalesTestFixture.Now.AddDays(1);
        var created = await new CreateAppointmentCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock,
                new AppointmentNotices(fixture.Context, new PhoneNormalizationService(), fixture.Clock))
            .Handle(new CreateAppointmentCommand
            {
                CustomerName = "Luis",
                CustomerPhone = "5512345678",
                ServiceName = serviceId is null ? "Visita" : null!,
                ServiceProductId = serviceId,
                StartAt = start,
                EndAt = start.AddHours(1),
                Visit = new AppointmentVisitInput { PostalCode = "72000", AddressLine = "Av. Juárez 123, Col. Centro" }
            }, CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error);
        return created.Value!;
    }

    /// <summary>Charges the visit with its service, paid in cash.</summary>
    private static async Task<OrderDto> SellVisitAsync(SalesTestFixture fixture, Domain.Tenants.Entities.Tenant tenant, Product service, Guid appointmentId)
    {
        await fixture.OpenCashSessionAsync(tenant);
        var inventory = new OrderInventoryService(fixture.Context, fixture.Clock, new FolioGenerator(fixture.Context),
            NullLogger<OrderInventoryService>.Instance);
        var settings = await fixture.Context.TenantSettings.AsNoTracking().SingleAsync(s => s.TenantId == tenant.Id);
        // 300 and the zone's 80, with IVA when the prices do not include it.
        var total = settings.PricesIncludeTax ? 380m : 440.80m;
        var sale = await fixture.CreateOrderHandler(inventory).Handle(new CreateOrderCommand
        {
            OrderType = OrderType.Service,
            CustomerName = "Luis",
            Items = [new CreateOrderItemDto { ProductId = service.Id, Quantity = 1 }],
            AppointmentId = appointmentId,
            Payments = [new CreateOrderPaymentDto { Amount = total, Method = OrderPaymentMethod.Cash }]
        }, CancellationToken.None);
        Assert.True(sale.IsSuccess, sale.Error);
        fixture.Context.ChangeTracker.Clear();
        return sale.Value!;
    }
}
