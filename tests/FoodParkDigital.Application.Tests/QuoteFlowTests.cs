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
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Customers.Entities;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.HomeDelivery;
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
    public async Task Create_ShouldQuoteOnlyAVisitOpenOrDone()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture);
        var atTheBusiness = await new CreateAppointmentCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser,
                fixture.Clock, new AppointmentNotices(fixture.Context, new PhoneNormalizationService(), fixture.Clock))
            .Handle(new CreateAppointmentCommand
            {
                CustomerName = "Rosa", CustomerPhone = "5511112222", ServiceName = "Revisión",
                StartAt = SalesTestFixture.Now.AddDays(2), EndAt = SalesTestFixture.Now.AddDays(2).AddHours(1)
            }, CancellationToken.None);
        var cancelled = await VisitAsync(fixture);
        await new UpdateAppointmentStatusCommandHandler(fixture.Context, new AppointmentNotices(fixture.Context, new PhoneNormalizationService(), fixture.Clock))
            .Handle(new UpdateAppointmentStatusCommand { AppointmentId = cancelled.Id, NewStatus = AppointmentStatus.Cancelled }, CancellationToken.None);
        var zone = await fixture.Context.DeliveryZones.Include(z => z.PostalCodes).SingleAsync();
        var requested = Domain.Appointments.Entities.Appointment.RequestHomeVisit(
            tenant.Id, "Valeria", "5522107765", null, "Visita a domicilio", SalesTestFixture.Now.AddDays(3), SalesTestFixture.Now.AddDays(3).AddHours(1),
            "Gotea el calentador", null, DeliveryAddress.ForVisit("72000", "Av. Juárez 123", null), zone, HomeVisitChargePolicy.Charged,
            null, Guid.NewGuid(), new string('r', 43));
        fixture.Context.Appointments.Add(requested);
        await fixture.Context.SaveChangesAsync();

        var missed = await VisitAsync(fixture, start: SalesTestFixture.Now.AddDays(4));
        var done = await VisitAsync(fixture, start: SalesTestFixture.Now.AddDays(5));
        var status = new UpdateAppointmentStatusCommandHandler(fixture.Context, new AppointmentNotices(fixture.Context, new PhoneNormalizationService(), fixture.Clock));
        await status.Handle(new UpdateAppointmentStatusCommand { AppointmentId = missed.Id, NewStatus = AppointmentStatus.NoShow }, CancellationToken.None);
        await status.Handle(new UpdateAppointmentStatusCommand { AppointmentId = done.Id, NewStatus = AppointmentStatus.Completed }, CancellationToken.None);

        Assert.Equal(QuoteRules.NotAVisitMessage, (await CreateAsync(fixture, new CreateQuoteCommand(atTheBusiness.Value!.Id, null, null, null, null))).Error);
        Assert.Equal(QuoteRules.VisitClosedMessage, (await CreateAsync(fixture, new CreateQuoteCommand(cancelled.Id, null, null, null, null))).Error);
        Assert.Equal(QuoteRules.VisitClosedMessage, (await CreateAsync(fixture, new CreateQuoteCommand(missed.Id, null, null, null, null))).Error);
        Assert.Equal(QuoteRules.VisitRequestedMessage, (await CreateAsync(fixture, new CreateQuoteCommand(requested.Id, null, null, null, null))).Error);
        Assert.Equal(0, await fixture.Context.Quotes.CountAsync());
        // The quote comes after the visit: a visit done is quoted.
        var afterTheVisit = await CreateAsync(fixture, new CreateQuoteCommand(done.Id, null, null, null, null));
        Assert.True(afterTheVisit.IsSuccess, afterTheVisit.Error);
        Assert.Equal(done.Id, afterTheVisit.Value!.Appointment!.Id);
    }

    [Fact]
    public async Task Draft_ShouldBeTotalledByTheServer_WithTheProductsIva_AndTheBusinessesForALineByHand()
    {
        await using var fixture = SalesTestFixture.Create();
        // A business at the border charges 8 %; the heater carries 16 % of its own.
        var tenant = await TradeAsync(fixture, pricesIncludeTax: false, defaultTaxRate: 8m);
        var heater = await fixture.AddProductAsync(tenant, "Cambio de calentador", 1000m, taxRate: 16m, unit: UnitType.Service);
        var pipe = await fixture.AddProductAsync(tenant, "Tubo de cobre", 50m, isTaxExempt: true);
        var quote = (await CreateAsync(fixture)).Value!;

        var saved = await UpdateAsync(fixture, quote, [
            new QuoteLineRequest { ProductId = heater.Id, Quantity = 1m, UnitPrice = 900m },
            new QuoteLineRequest { ProductId = pipe.Id, Description = "Tubo de cobre de 1/2", Quantity = 3m, UnitPrice = 50m },
            new QuoteLineRequest { Description = "Mano de obra", Quantity = 2.5m, UnitPrice = 200m }]);

        Assert.True(saved.IsSuccess, saved.Error);
        var lines = saved.Value!.Lines;
        Assert.Equal([("Cambio de calentador", 900m, 16m, false), ("Tubo de cobre de 1/2", 150m, 0m, true), ("Mano de obra", 500m, 8m, false)],
            lines.Select(l => (l.Description, l.Total, l.TaxRate, l.IsTaxExempt)));
        // 900 at 16 % adds 144, the labour's 500 at 8 % adds 40, and the exempt 150 adds nothing.
        Assert.Equal((1550m, 184m, 1734m, 1734m), (saved.Value.SubTotal, saved.Value.Tax, saved.Value.Total, saved.Value.ToPay));
        Assert.Equal([(0m, true, 150m, 0m), (8m, false, 500m, 40m), (16m, false, 900m, 144m)],
            saved.Value.Taxes.Select(t => (t.RatePercent, t.IsExempt, t.TaxableBase, t.TaxAmount)).OrderBy(t => t.RatePercent));
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
    public async Task TheBusinessesEvening_ShouldStillBeItsDay_ForTheValidity_TheExpiry_AndTheLink()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var quote = await DraftWithALineAsync(fixture);
        // October 4 at 19:00 in Mexico City is already October 5 in UTC.
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 10, 5, 1, 0, 0, TimeSpan.Zero);

        var sent = (await Handler(fixture).Handle(new SendQuoteCommand(quote.Id), CancellationToken.None)).Value!;

        // Fifteen days from the business's October 4.
        Assert.Equal(new DateOnly(2026, 10, 19), sent.ValidUntil);
        // October 19 at 21:00 there (the 20th in UTC) it is still valid; past its midnight it expired.
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 10, 20, 3, 0, 0, TimeSpan.Zero);
        Assert.Equal(QuoteStatus.Sent, (await GetAsync(fixture, sent.Id)).Status);
        Assert.Equal(QuoteStatus.Sent, (await PublicAsync(fixture, sent.TrackingToken!))!.Status);
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 10, 20, 7, 0, 0, TimeSpan.Zero);
        Assert.Equal(QuoteStatus.Expired, (await GetAsync(fixture, sent.Id)).Status);
        // Its link opens through November 18 there, 30 days after its validity, and not the day after.
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 11, 19, 3, 0, 0, TimeSpan.Zero);
        Assert.NotNull(await PublicAsync(fixture, sent.TrackingToken!));
        fixture.Clock.UtcNow = new DateTimeOffset(2026, 11, 19, 7, 0, 0, TimeSpan.Zero);
        Assert.Null(await PublicAsync(fixture, sent.TrackingToken!));
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
    public async Task Accepting_ShouldDiscardACorrectionStartedOnIt_WhichCouldNeverBeSent()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture);
        var sent = await SentAsync(fixture);
        var correction = (await Handler(fixture).Handle(new ReviseQuoteCommand(sent.Id), CancellationToken.None)).Value!;

        await Handler(fixture).Handle(new RecordQuoteReplyCommand(sent.Id, true, QuoteReplyChannel.WhatsApp, null), CancellationToken.None);

        Assert.Equal(QuoteStatus.Discarded, (await GetAsync(fixture, correction.Id)).Status);
        Assert.Empty((await Handler(fixture).Handle(new ListQuotesQuery(QuoteListFilter.Open), CancellationToken.None)).Items);
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

    [Fact]
    public async Task TheVisit_ShouldComeOff_OnlyWhatItsSaleStillHasPaid_AfterARefund()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture, pricesIncludeTax: false, policy: HomeVisitChargePolicy.CreditedIfHired);
        var service = await fixture.AddProductAsync(tenant, "Visita y diagnóstico", 300m, unit: UnitType.Service);
        var visit = await VisitAsync(fixture, service.Id);
        var sale = await SellVisitAsync(fixture, tenant, service, visit.Id);
        var quote = (await CreateAsync(fixture, new CreateQuoteCommand(visit.Id, null, null, null, null))).Value!;
        await UpdateAsync(fixture, quote, [new QuoteLineRequest { Description = "Cambio de calentador", Quantity = 1m, UnitPrice = 1000m }]);
        var sent = (await Handler(fixture).Handle(new SendQuoteCommand(quote.Id), CancellationToken.None)).Value!;
        Assert.Equal((440.80m, true, 719.20m), (sent.VisitCredit!.Amount, sent.VisitCredit.Applies, sent.ToPay));

        // The sale stays completed: a refund only lowers what it has paid.
        await RefundAsync(fixture, sale.Id, 140.80m);
        var partly = await GetAsync(fixture, sent.Id);
        var partlyPublic = await PublicAsync(fixture, sent.TrackingToken!);
        Assert.Equal((300m, true, 860m), (partly.VisitCredit!.Amount, partly.VisitCredit.Applies, partly.ToPay));
        Assert.Equal((300m, true, 860m), (partlyPublic!.VisitCredit!.Amount, partlyPublic.VisitCredit.Applies, partlyPublic.ToPay));

        await RefundAsync(fixture, sale.Id, 300m);
        var refunded = await GetAsync(fixture, sent.Id);
        var refundedPublic = await PublicAsync(fixture, sent.TrackingToken!);
        Assert.Equal(OrderStatus.Completed, (await fixture.Context.Orders.AsNoTracking().SingleAsync(o => o.Id == sale.Id)).Status);
        Assert.Equal((440.80m, false, 1160m), (refunded.VisitCredit!.Amount, refunded.VisitCredit.Applies, refunded.ToPay));
        Assert.Equal((false, 1160m), (refundedPublic!.VisitCredit!.Applies, refundedPublic.ToPay));
    }

    [Fact]
    public async Task TheVisit_ShouldNotComeOff_ForTheServiceReturned_EvenWhileTheSaleKeepsAPartPaid()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture, pricesIncludeTax: false, policy: HomeVisitChargePolicy.CreditedIfHired);
        var service = await fixture.AddProductAsync(tenant, "Visita y diagnóstico", 300m, unit: UnitType.Service);
        var part = await fixture.AddProductAsync(tenant, "Válvula de paso", 200m);
        var returnedFirst = await VisitAsync(fixture, service.Id);
        var returnedAfter = await VisitAsync(fixture, service.Id, SalesTestFixture.Now.AddDays(2));
        // Each sale charged the visit (its service and its zone, $440.80) and a part ($232): $672.80, all paid.
        var firstSale = await SellVisitAsync(fixture, tenant, service, returnedFirst.Id, extra: part);
        var afterSale = await SellVisitAsync(fixture, tenant, service, returnedAfter.Id, extra: part);
        var lines = new[] { new QuoteLineRequest { Description = "Cambio de calentador", Quantity = 1m, UnitPrice = 1000m } };

        // The service ($348 with IVA) is returned before quoting: only the zone's $92.80 is left paid for the visit.
        await RefundAsync(fixture, firstSale.Id, 348m, firstSale.Items.Single(i => i.ProductId == service.Id).Id);
        var first = (await CreateAsync(fixture, new CreateQuoteCommand(returnedFirst.Id, null, null, null, null))).Value!;
        first = (await UpdateAsync(fixture, first, [.. lines])).Value!;
        Assert.Equal((92.80m, true, 1067.20m), (first.VisitCredit!.Amount, first.VisitCredit.Applies, first.ToPay));

        // Returned once the quote was sent with $440.80 off: the same $92.80 come off, also through its link.
        var after = (await CreateAsync(fixture, new CreateQuoteCommand(returnedAfter.Id, null, null, null, null))).Value!;
        await UpdateAsync(fixture, after, [.. lines]);
        var sent = (await Handler(fixture).Handle(new SendQuoteCommand(after.Id), CancellationToken.None)).Value!;
        Assert.Equal((440.80m, 719.20m), (sent.VisitCredit!.Amount, sent.ToPay));
        await RefundAsync(fixture, afterSale.Id, 348m, afterSale.Items.Single(i => i.ProductId == service.Id).Id);
        var read = await GetAsync(fixture, sent.Id);
        var link = await PublicAsync(fixture, sent.TrackingToken!);
        Assert.Equal((92.80m, true, 1067.20m), (read.VisitCredit!.Amount, read.VisitCredit.Applies, read.ToPay));
        Assert.Equal((92.80m, 1067.20m), (link!.VisitCredit!.Amount, link.ToPay));
    }

    [Fact]
    public async Task TheVisit_ShouldComeOff_OnlyWhatItsSalePaid_AndOnlyWhatWasForTheVisit()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await TradeAsync(fixture, pricesIncludeTax: false, policy: HomeVisitChargePolicy.CreditedIfHired);
        var service = await fixture.AddProductAsync(tenant, "Visita y diagnóstico", 300m, unit: UnitType.Service);
        var part = await fixture.AddProductAsync(tenant, "Válvula de paso", 200m);
        var withAdvance = await VisitAsync(fixture, service.Id);
        var withPart = await VisitAsync(fixture, service.Id, SalesTestFixture.Now.AddDays(2));
        // An advance of $100 on the $440.80 of the visit; and a visit that also sold a part, all paid ($672.80).
        await SellVisitAsync(fixture, tenant, service, withAdvance.Id, paid: 100m);
        await SellVisitAsync(fixture, tenant, service, withPart.Id, extra: part);

        var advanced = (await CreateAsync(fixture, new CreateQuoteCommand(withAdvance.Id, null, null, null, null))).Value!;
        var parted = (await CreateAsync(fixture, new CreateQuoteCommand(withPart.Id, null, null, null, null))).Value!;
        advanced = (await UpdateAsync(fixture, advanced, [new QuoteLineRequest { Description = "Cambio de calentador", Quantity = 1m, UnitPrice = 1000m }])).Value!;
        parted = (await UpdateAsync(fixture, parted, [new QuoteLineRequest { Description = "Cambio de calentador", Quantity = 1m, UnitPrice = 1000m }])).Value!;

        // Only what was paid comes off; and of a sale that also sold a part, only the visit (its service and its zone).
        Assert.Equal((100m, 1060m), (advanced.VisitCredit!.Amount, advanced.ToPay));
        Assert.Equal((440.80m, 719.20m), (parted.VisitCredit!.Amount, parted.ToPay));
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
    public async Task AVisitOfAnotherBusiness_ShouldNotBeQuoted()
    {
        await using var fixture = SalesTestFixture.Create();
        await TradeAsync(fixture, slug: "otro");
        var theirs = await VisitAsync(fixture);
        await TradeAsync(fixture);

        var refused = await CreateAsync(fixture, new CreateQuoteCommand(theirs.Id, null, null, null, null));

        Assert.Equal(QuotesHandler.AppointmentNotFoundMessage, refused.Error);
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
        await Assert.ThrowsAsync<EntityNotFoundException>(() => UpdateAsync(fixture, theirs, []));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => Handler(fixture).Handle(new SendQuoteCommand(theirs.Id), CancellationToken.None));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => Handler(fixture).Handle(new DiscardQuoteCommand(theirs.Id), CancellationToken.None));
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
        // Lines missing, or a missing line, are refused instead of breaking; a description that fits once trimmed passes.
        var validator = new UpdateQuoteDraftCommandValidator();
        Assert.False(validator.Validate(new UpdateQuoteDraftCommand(Guid.NewGuid(), null, "Valeria", "5522107765", null, 15, null, null!)).IsValid);
        Assert.False(validator.Validate(new UpdateQuoteDraftCommand(Guid.NewGuid(), null, "Valeria", "5522107765", null, 15, null, [null!])).IsValid);
        Assert.True(validator.Validate(new UpdateQuoteDraftCommand(Guid.NewGuid(), null, "Valeria", "5522107765", null, 15, null,
            [new QuoteLineRequest { Description = $"  {new string('a', 200)}  ", Quantity = 1m, UnitPrice = 1m }])).IsValid);
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
        HomeVisitChargePolicy policy = HomeVisitChargePolicy.Charged, decimal defaultTaxRate = 16m)
    {
        var tenant = await fixture.AddTenantAsync(
            BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments, pricesIncludeTax: pricesIncludeTax,
            defaultTaxRate: defaultTaxRate, slug: slug);
        (await fixture.Context.TenantSettings.SingleAsync(s => s.TenantId == tenant.Id)).UpdateHomeVisitRequests(true, null, policy);
        fixture.Context.DeliveryZones.Add(new DeliveryZone(tenant.Id, new DeliveryZoneFields("Centro", 80m, 0m, null, ["72000"])));
        await fixture.Context.SaveChangesAsync();
        return tenant;
    }

    /// <summary>A visit the team scheduled at 72000 (tomorrow unless said), with the service when given.</summary>
    private static async Task<AppointmentDto> VisitAsync(SalesTestFixture fixture, Guid? serviceId = null, DateTimeOffset? start = null)
    {
        var startAt = start ?? SalesTestFixture.Now.AddDays(1);
        var created = await new CreateAppointmentCommandHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock,
                new AppointmentNotices(fixture.Context, new PhoneNormalizationService(), fixture.Clock))
            .Handle(new CreateAppointmentCommand
            {
                CustomerName = "Luis",
                CustomerPhone = "5512345678",
                ServiceName = serviceId is null ? "Visita" : null!,
                ServiceProductId = serviceId,
                StartAt = startAt,
                EndAt = startAt.AddHours(1),
                Visit = new AppointmentVisitInput { PostalCode = "72000", AddressLine = "Av. Juárez 123, Col. Centro" }
            }, CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error);
        return created.Value!;
    }

    /// <summary>Charges the visit with its service, paid in cash.</summary>
    /// <summary>Charges the visit with its service (and <paramref name="extra"/>, when given), paid in cash: all of it unless <paramref name="paid"/>.</summary>
    /// <summary>Gives back part of what a sale was paid in cash, with the units returned if any, as the business does from its detail.</summary>
    private static async Task RefundAsync(SalesTestFixture fixture, Guid saleId, decimal amount, params Guid[] returnedItemIds)
    {
        fixture.Context.ChangeTracker.Clear();
        var refund = await new RefundOrderCommandHandler(
                fixture.Context, fixture.CurrentUser, new Finance.OrderCashService(fixture.Context, fixture.CurrentUser),
                fixture.Clock, NoOpOrderInventoryService.Instance)
            .Handle(new RefundOrderCommand
            {
                OrderId = saleId,
                Amount = amount,
                Method = OrderPaymentMethod.Cash,
                Reason = "No se hizo el trabajo",
                Items = [.. returnedItemIds.Select(id => new RefundItemDto { OrderItemId = id, Quantity = 1m })]
            }, CancellationToken.None);
        Assert.True(refund.IsSuccess, refund.Error);
        fixture.Context.ChangeTracker.Clear();
    }

    private static async Task<OrderDto> SellVisitAsync(
        SalesTestFixture fixture, Domain.Tenants.Entities.Tenant tenant, Product service, Guid appointmentId, Product? extra = null, decimal? paid = null)
    {
        // One drawer open for every sale of the test.
        if (!await fixture.Context.CashSessions.AnyAsync(c => c.TenantId == tenant.Id))
            await fixture.OpenCashSessionAsync(tenant);
        var inventory = new OrderInventoryService(fixture.Context, fixture.Clock, new FolioGenerator(fixture.Context),
            NullLogger<OrderInventoryService>.Instance);
        var settings = await fixture.Context.TenantSettings.AsNoTracking().SingleAsync(s => s.TenantId == tenant.Id);
        // 300 and the zone's 80 (plus what the extra costs), with IVA when the prices do not include it.
        var amount = 380m + (extra?.PriceAmount ?? 0m);
        var total = settings.PricesIncludeTax ? amount : amount * 1.16m;
        CreateOrderItemDto[] items = extra is null
            ? [new CreateOrderItemDto { ProductId = service.Id, Quantity = 1 }]
            : [new CreateOrderItemDto { ProductId = service.Id, Quantity = 1 }, new CreateOrderItemDto { ProductId = extra.Id, Quantity = 1 }];
        var sale = await fixture.CreateOrderHandler(inventory).Handle(new CreateOrderCommand
        {
            OrderType = OrderType.Service,
            CustomerName = "Luis",
            Items = [.. items],
            AppointmentId = appointmentId,
            Payments = [new CreateOrderPaymentDto { Amount = paid ?? total, Method = OrderPaymentMethod.Cash }]
        }, CancellationToken.None);
        Assert.True(sale.IsSuccess, sale.Error);
        fixture.Context.ChangeTracker.Clear();
        return sale.Value!;
    }
}
