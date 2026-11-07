using FoodParkDigital.Application.Appointments.RequestNotices;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Quotes.ReplyNotices;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Events;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Quotes.Events;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static FoodParkDigital.Application.Tests.ExploreResponseTimeTests;
using static FoodParkDigital.Application.Tests.Support.NoticeEmailsSupport;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// Telling the business what its customers answer online (Fase 29O, ADR 0069): an email with each appointment its customer
/// cancels, to whoever gets the agenda's notices, and a notice on its live screens; an email with each answer to a quote from
/// its link, to whoever gets those and may work the quotes; what they say and never say; and each person's own choice.
/// </summary>
public class CustomerAnswerNoticesTests
{
    // Now: 2027-03-01 at 11:00 in Mexico City, a Monday.
    private static readonly DateOnly Today = new(2027, 3, 1);

    [Fact]
    public async Task TheEmailsOfACancellation_ShouldGoToWhoGetsTheAgendasNotices_AndMayAnswerTheRequests()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "cancela", "Estética Cancela");
        var owner = await MemberAsync(fixture, salon, "dueña", TenantUserRole.Owner);
        await MemberAsync(fixture, salon, "socia", TenantUserRole.Owner, choose: false);
        var stylist = await MemberAsync(fixture, salon, "estilista", TenantUserRole.Staff, choose: true, permissions: [TenantPermission.ManageAppointments]);
        await MemberAsync(fixture, salon, "cajero", TenantUserRole.Staff, choose: true, permissions: [TenantPermission.ManageOrders]);
        // Choosing the answers to quotes does not bring the agenda's notices.
        await MemberAsync(fixture, salon, "admin", TenantUserRole.Admin, chooseQuotes: true);
        var other = await SalonAsync(fixture, "otra", "Estética Otra");
        await MemberAsync(fixture, other, "otra", TenantUserRole.Owner);
        var request = await RequestAsync(fixture, salon, Now.AddHours(-1));
        request.CancelByCustomer(Now.AddMinutes(-1));
        var accepted = await RequestAsync(fixture, salon, Now.AddHours(-1));
        accepted.Accept(Now.AddMinutes(-30));
        accepted.CancelByCustomer(Now.AddMinutes(-1));
        await fixture.Context.SaveChangesAsync();

        var ofRequest = await QueueCancellationAsync(fixture, salon, request, wasRequest: true);
        var ofAccepted = await QueueCancellationAsync(fixture, salon, accepted, wasRequest: false);

        Assert.Equal(new[] { owner, stylist }.Order(), ofRequest.Select(q => q.Event.UserId).Order());
        Assert.All(ofRequest, q => Assert.Equal((request.Id, true, (Guid?)salon.Id), (q.Event.AppointmentId, q.Event.WasRequest, q.TenantId)));
        Assert.Equal(new[] { owner, stylist }.Order(), ofAccepted.Select(q => q.Event.UserId).Order());
        Assert.All(ofAccepted, q => Assert.False(q.Event.WasRequest));
    }

    [Fact]
    public async Task NoEmail_ShouldGo_ForACancellationOfTheTeam_OneOverADayOld_OneWhoseTimeCame_OrOneNotByItsCustomer()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "nada", "Estética Nada");
        await MemberAsync(fixture, salon, "dueña", TenantUserRole.Owner);
        var ofTheTeam = await CancelledAsync(fixture, salon, Now.AddMinutes(-1), fromTeam: true);
        var overADay = await CancelledAsync(fixture, salon, Now.AddDays(-1).AddMinutes(-1));
        var itsTimeCame = await CancelledAsync(fixture, salon, Now.AddHours(-2), startAt: Now);
        var declined = await RequestAsync(fixture, salon, Now.AddHours(-1));
        declined.Decline(Now.AddMinutes(-1));
        // Cancelled a day ago to the minute, and its time a minute away: still worth it.
        var aDay = await CancelledAsync(fixture, salon, Now.AddDays(-1));
        var aMinuteAway = await CancelledAsync(fixture, salon, Now.AddHours(-2), startAt: Now.AddMinutes(1));
        await fixture.Context.SaveChangesAsync();

        foreach (var none in new[] { ofTheTeam, overADay, itsTimeCame, declined })
            Assert.Empty(await QueueCancellationAsync(fixture, salon, none, wasRequest: true));
        Assert.Single(await QueueCancellationAsync(fixture, salon, aDay, wasRequest: true));
        Assert.Single(await QueueCancellationAsync(fixture, salon, aMinuteAway, wasRequest: true));

        // Nor from a business suspended since.
        salon.SetStatus(TenantStatus.Suspended);
        await fixture.Context.SaveChangesAsync();
        Assert.Empty(await QueueCancellationAsync(fixture, salon, aDay, wasRequest: true));
    }

    [Fact]
    public async Task TheEmailOfACancelledRequest_ShouldSayWhatWhenAndWho_InTheBusinessZone_AndNeverThePhoneOrTheNotes()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "texto", "Estética <Luna> & Sol");
        var owner = await MemberAsync(fixture, salon, "dueña", TenantUserRole.Owner);
        var thursday = new DateTimeOffset(2027, 3, 4, 13, 30, 0, TimeSpan.FromHours(-6));
        var request = Appointment.RequestOnline(
            salon.Id, "Ana <b>\"Leo\"</b>\nRuiz", "+525522107765", Guid.NewGuid(), "Corte & barba", thursday, thursday.AddHours(1),
            "Tengo el cabello muy largo", null, Guid.NewGuid(), new string('t', 43));
        request.CreatedAt = Now.AddHours(-1);
        request.CancelByCustomer(Now.AddMinutes(-1));
        fixture.Context.Appointments.Add(request);
        await fixture.Context.SaveChangesAsync();

        var sent = Assert.Single(await SendCancellationAsync(fixture, salon, request, owner, wasRequest: true));

        Assert.Equal($"dueña.{salon.Slug}@demo.com", sent.To);
        Assert.Equal("Solicitud de cita cancelada en Estética <Luna> & Sol: Corte & barba, jueves 4 de marzo a la 1:30 p. m.", sent.Subject);
        Assert.Contains("<h2>Se canceló una solicitud de cita</h2>", sent.Body);
        Assert.Contains(
            "<p><strong>Ana &lt;b&gt;&quot;Leo&quot;&lt;/b&gt; Ruiz</strong> canceló la solicitud de <strong>Corte &amp; barba</strong> que hizo en Est&#233;tica &lt;Luna&gt; &amp; Sol para el jueves 4 de marzo a la 1:30 p. m.</p>",
            sent.Body);
        Assert.Contains("<p>Ese horario quedó libre en la Agenda, y ya no tienes que responderla.</p>", sent.Body);
        Assert.Contains($"<a href='{AppUrl}/appointments'>Abrir la Agenda</a>", sent.Body);
        Assert.Contains(
            $"Te llega porque recibes un correo con cada solicitud en línea de Est&#233;tica &lt;Luna&gt; &amp; Sol, un recordatorio de las que siguen sin respuesta y uno con cada cita que sus clientes cancelan. Puedes dejar de recibirlos en <a href='{AppUrl}/profile'>Mi perfil</a>.",
            sent.Body);
        Assert.DoesNotContain("5522107765", sent.Body + sent.Subject);
        Assert.DoesNotContain("largo", sent.Body);
    }

    [Fact]
    public async Task TheEmailOfACancelledAppointmentOrVisit_ShouldSayItsTimeIsFree_WithoutTheVisitsAddress()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "cita", "Estética Cita");
        var owner = await MemberAsync(fixture, salon, "dueña", TenantUserRole.Owner);
        var accepted = await RequestAsync(fixture, salon, Now.AddHours(-3), startAt: new DateTimeOffset(2027, 3, 2, 9, 0, 0, TimeSpan.FromHours(-6)));
        accepted.Accept(Now.AddHours(-2));
        accepted.CancelByCustomer(Now.AddMinutes(-1));
        var (trade, zone) = await TradeAsync(fixture, "plomeria", "Plomería Rápida");
        var plumber = await MemberAsync(fixture, trade, "dueño", TenantUserRole.Owner);
        var tomorrow = new DateTimeOffset(2027, 3, 2, 9, 0, 0, TimeSpan.FromHours(-6));
        var visit = Appointment.RequestHomeVisit(
            trade.Id, "Valeria", "+525522107765", null, HomeVisitRules.DefaultVisitName, tomorrow, tomorrow.AddHours(1), "Gotea el calentador",
            null, DeliveryAddress.ForVisit("72000", "Av. Juárez 123, Col. Centro", null), zone, HomeVisitChargePolicy.Charged, null,
            Guid.NewGuid(), new string('v', 43));
        visit.CreatedAt = Now.AddHours(-3);
        visit.Accept(Now.AddHours(-2));
        visit.CancelByCustomer(Now.AddMinutes(-1));
        fixture.Context.Appointments.Add(visit);
        await fixture.Context.SaveChangesAsync();

        var ofAppointment = Assert.Single(await SendCancellationAsync(fixture, salon, accepted, owner, wasRequest: false));
        var ofVisit = Assert.Single(await SendCancellationAsync(fixture, trade, visit, plumber, wasRequest: false, appUrl: null));

        Assert.Equal("Cita cancelada en Estética Cita: Corte, martes 2 de marzo a las 9:00 a. m.", ofAppointment.Subject);
        Assert.Contains("<h2>Se canceló una cita</h2>", ofAppointment.Body);
        Assert.Contains("<p><strong>Valeria</strong> canceló su cita de <strong>Corte</strong> en Est&#233;tica Cita del martes 2 de marzo a las 9:00 a. m.</p>", ofAppointment.Body);
        Assert.Contains("<p>Ese horario quedó libre en la Agenda.</p>", ofAppointment.Body);

        Assert.Equal("Visita cancelada en Plomería Rápida: Visita a domicilio, martes 2 de marzo a las 9:00 a. m.", ofVisit.Subject);
        Assert.Contains("<h2>Se canceló una visita</h2>", ofVisit.Body);
        // Its service is the visit itself: not said twice.
        Assert.Contains("<p><strong>Valeria</strong> canceló la visita a domicilio de Plomer&#237;a R&#225;pida del martes 2 de marzo a las 9:00 a. m.</p>", ofVisit.Body);
        // Without the app's address configured, no links.
        Assert.Contains("<p>Revísala en la Agenda de TienditApp.</p>", ofVisit.Body);
        Assert.DoesNotContain("href", ofVisit.Body);
        Assert.DoesNotContain("Juárez", ofVisit.Body);
        Assert.DoesNotContain("72000", ofVisit.Body);
        Assert.DoesNotContain("calentador", ofVisit.Body);
    }

    [Fact]
    public void TheEmailOfACancelledVisitRequest_ShouldNameItsService_WhenTheBusinessNamedItOtherwise()
    {
        var start = new DateTimeOffset(2027, 3, 2, 9, 0, 0, TimeSpan.FromHours(-6));

        var email = CancellationEmail.Compose(
            "Plomería", "Valeria", "Revisión de fugas", homeVisit: true, wasRequest: true, start, "America/Mexico_City", AppUrl);

        Assert.Equal("Solicitud de visita cancelada en Plomería: Revisión de fugas, martes 2 de marzo a las 9:00 a. m.", email.Subject);
        Assert.Contains("<h2>Se canceló una solicitud de visita</h2>", email.HtmlBody);
        Assert.Contains("<p><strong>Valeria</strong> canceló la solicitud de visita a domicilio (Revisi&#243;n de fugas) que le hizo a Plomer&#237;a para el martes 2 de marzo a las 9:00 a. m.</p>", email.HtmlBody);
    }

    [Fact]
    public async Task TheEmailOfACancellation_ShouldNotGo_WhenTheMemberStoppedGettingThem_ItsTimeCame_OrItIsOfAnotherBusiness()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "despues", "Estética Después");
        var owner = await MemberAsync(fixture, salon, "dueña", TenantUserRole.Owner);
        var admin = await MemberAsync(fixture, salon, "admin", TenantUserRole.Admin, choose: true);
        var cancelled = await CancelledAsync(fixture, salon, Now.AddMinutes(-5));
        var soon = await CancelledAsync(fixture, salon, Now.AddHours(-1), startAt: Now.AddMinutes(30));

        (await fixture.Context.TenantUsers.SingleAsync(m => m.UserId == admin)).ChooseRequestEmails(false);
        await fixture.Context.SaveChangesAsync();

        Assert.Empty(await SendCancellationAsync(fixture, salon, cancelled, admin, wasRequest: true));
        Assert.Single(await SendCancellationAsync(fixture, salon, cancelled, owner, wasRequest: true));
        // Its time came while it waited in the outbox.
        fixture.Clock.UtcNow = Now.AddMinutes(30);
        Assert.Empty(await SendCancellationAsync(fixture, salon, soon, owner, wasRequest: true));
        fixture.Clock.UtcNow = Now;
        // One of the team never gets one, though it were queued; nor one queued for a member of another business.
        var ofTheTeam = await CancelledAsync(fixture, salon, Now.AddMinutes(-1), fromTeam: true);
        Assert.Empty(await SendCancellationAsync(fixture, salon, ofTheTeam, owner, wasRequest: true));
        var other = await SalonAsync(fixture, "otra", "Estética Otra");
        Assert.Empty(await SendCancellationAsync(fixture, other, cancelled, owner, wasRequest: true));
    }

    [Fact]
    public async Task AFailureOfTheProvider_ShouldThrow_ForTheOutboxToRetryThatEmailAlone()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "falla", "Estética Falla");
        var owner = await MemberAsync(fixture, salon, "dueña", TenantUserRole.Owner);
        var cancelled = await CancelledAsync(fixture, salon, Now.AddMinutes(-1));
        var (quote, trade, plumber) = await AnsweredQuoteAsync(fixture, accepted: true);
        fixture.TenantService.SetTenant(salon.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new AppointmentCancellationEmailHandler(
                fixture.Context, new MembersAccess(fixture.Context), new Emails(fail: true), new Links(AppUrl), fixture.Clock)
            .HandleAsync(new AppointmentCancellationEmailEvent(cancelled.Id, owner, true), Context(salon), CancellationToken.None));
        fixture.TenantService.SetTenant(trade.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new QuoteReplyEmailHandler(
                fixture.Context, new MembersAccess(fixture.Context), new Emails(fail: true), new Links(AppUrl), fixture.Clock)
            .HandleAsync(new QuoteReplyEmailEvent(quote.Id, plumber), Context(trade), CancellationToken.None));
    }

    [Fact]
    public async Task TheLiveScreens_ShouldHearWhatWasCancelledAndWhen_AndNotWhoCancelledIt()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "vivo", "Estética Vivo");
        var cancelled = await CancelledAsync(fixture, salon, Now.AddMinutes(-1));
        fixture.TenantService.SetTenant(salon.Id);
        var screens = new Screens();

        // What the event says of it (here, that it was already agendada) is what the screens hear.
        await Notifications(fixture, screens)
            .Handle(new AppointmentCancelledByCustomerEvent(cancelled.Id, salon.Id, HomeVisit: false, FromTeam: false, WasRequest: false), CancellationToken.None);
        // One of another business is never told to this one.
        await Notifications(fixture, screens)
            .Handle(new AppointmentCancelledByCustomerEvent(cancelled.Id, Guid.NewGuid(), HomeVisit: false, FromTeam: false, WasRequest: true), CancellationToken.None);
        // A screen that fails does not fail the cancellation.
        await Notifications(fixture, new Screens(fail: true))
            .Handle(new AppointmentCancelledByCustomerEvent(cancelled.Id, salon.Id, HomeVisit: false, FromTeam: false, WasRequest: true), CancellationToken.None);

        var (tenantId, notice) = Assert.Single(screens.Cancelled);
        Assert.Equal(salon.Id, tenantId);
        Assert.Equal(new AppointmentCancelledNotice(cancelled.Id, "Corte", cancelled.StartAt, false, false, false), notice);
        Assert.Empty(screens.Heard);
    }

    [Fact]
    public async Task TheEmailsOfAnAnswerToAQuote_ShouldGoToWhoGetsThem_AndMayWorkTheQuotes()
    {
        await using var fixture = Fixture();
        var (quote, trade, owner) = await AnsweredQuoteAsync(fixture, accepted: true);
        await MemberAsync(fixture, trade, "socia", TenantUserRole.Owner, chooseQuotes: false);
        var cashier = await MemberAsync(fixture, trade, "cajero", TenantUserRole.Staff, chooseQuotes: true, permissions: [TenantPermission.ManageOrders]);
        await MemberAsync(fixture, trade, "agenda", TenantUserRole.Staff, chooseQuotes: true, permissions: [TenantPermission.ManageAppointments]);
        await MemberAsync(fixture, trade, "admin", TenantUserRole.Admin);
        // Choosing the agenda's notices does not bring the answers to quotes.
        await MemberAsync(fixture, trade, "admin2", TenantUserRole.Admin, choose: true);
        await MemberAsync(fixture, trade, "sinconfirmar", TenantUserRole.Owner, emailConfirmed: false);
        var other = await SalonAsync(fixture, "otra", "Estética Otra");
        await MemberAsync(fixture, other, "otra", TenantUserRole.Owner);

        var queued = await QueueReplyAsync(fixture, trade, quote, accepted: true);

        Assert.Equal(new[] { owner, cashier }.Order(), queued.Select(q => q.Event.UserId).Order());
        Assert.All(queued, q => Assert.Equal((quote.Id, (Guid?)trade.Id), (q.Event.QuoteId, q.TenantId)));
    }

    [Fact]
    public async Task NoEmail_ShouldGo_ForAnAnswerTheBusinessRecorded_OneOverADayOld_OrOneTheBusinessAlreadyActedOn()
    {
        await using var fixture = Fixture();
        var (trade, _) = await TradeAsync(fixture, "plomero", "Plomería Ruiz");
        await MemberAsync(fixture, trade, "dueño", TenantUserRole.Owner);
        var byPhone = await SentQuoteAsync(fixture, trade, 1);
        byPhone.Accept(Today, QuoteReplyChannel.Phone, null, Now.AddMinutes(-1));
        var overADay = await SentQuoteAsync(fixture, trade, 2, sentAt: Now.AddDays(-2), today: Today.AddDays(-2));
        overADay.ReplyFromLink(true, Today.AddDays(-1), null, Now.AddDays(-1).AddMinutes(-1));
        var converted = await SentQuoteAsync(fixture, trade, 3);
        converted.ReplyFromLink(true, Today, null, Now.AddMinutes(-5));
        converted.ConvertToOrder(Guid.NewGuid(), 0m, Now.AddMinutes(-1));
        var replaced = await SentQuoteAsync(fixture, trade, 4);
        replaced.ReplyFromLink(false, Today, null, Now.AddMinutes(-5));
        var correction = replaced.Revise(2, Now.AddMinutes(-3));
        fixture.Context.Quotes.Add(correction);
        correction.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 990m, VatRate.General)], Now.AddMinutes(-3));
        correction.Send(Today, new string('r', 43), replaced, Now.AddMinutes(-2));
        // Answered a day ago to the minute: still worth it.
        var aDay = await SentQuoteAsync(fixture, trade, 5, sentAt: Now.AddDays(-2), today: Today.AddDays(-2));
        aDay.ReplyFromLink(false, Today.AddDays(-1), null, Now.AddDays(-1));
        await fixture.Context.SaveChangesAsync();

        Assert.Empty(await QueueReplyAsync(fixture, trade, byPhone, accepted: true));
        Assert.Empty(await QueueReplyAsync(fixture, trade, overADay, accepted: true));
        Assert.Empty(await QueueReplyAsync(fixture, trade, converted, accepted: true));
        Assert.Empty(await QueueReplyAsync(fixture, trade, replaced, accepted: false));
        Assert.Single(await QueueReplyAsync(fixture, trade, aDay, accepted: false));

        // Nor from a business suspended since, or one without services or appointments.
        trade.SetStatus(TenantStatus.Suspended);
        await fixture.Context.SaveChangesAsync();
        Assert.Empty(await QueueReplyAsync(fixture, trade, aDay, accepted: false));
        var shop = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: "tienda");
        await MemberAsync(fixture, shop, "dueño", TenantUserRole.Owner);
        var ofShop = await SentQuoteAsync(fixture, shop, 1);
        ofShop.ReplyFromLink(true, Today, null, Now.AddMinutes(-1));
        await fixture.Context.SaveChangesAsync();
        Assert.Empty(await QueueReplyAsync(fixture, shop, ofShop, accepted: true));
    }

    [Fact]
    public async Task TheEmailOfAnAcceptedQuote_ShouldSayWhoWhichForHowMuchAndWhatFollows_AndNeverWhatTheCustomerWrote()
    {
        await using var fixture = Fixture();
        var (quote, trade, owner) = await AnsweredQuoteAsync(fixture, accepted: true, business: "Plomería <Ruiz> & Hijos",
            customer: "Ana <b>\"Leo\"</b>\nRuiz", response: "Los espero el jueves, mi número es 5522107765");

        var sent = Assert.Single(await SendReplyAsync(fixture, trade, quote, owner));

        Assert.Equal($"dueño.{trade.Slug}@demo.com", sent.To);
        Assert.Equal("Cotización aceptada en Plomería <Ruiz> & Hijos: COT-000007 por $1,160.00", sent.Subject);
        Assert.Contains("<h2>Te aceptaron una cotización</h2>", sent.Body);
        Assert.Contains(
            "<p><strong>Ana &lt;b&gt;&quot;Leo&quot;&lt;/b&gt; Ruiz</strong> aceptó desde su enlace la cotización <strong>COT-000007</strong> de Plomer&#237;a &lt;Ruiz&gt; &amp; Hijos, por un total de <strong>$1,160.00</strong>.</p>",
            sent.Body);
        Assert.Contains("<p>Te dejó un mensaje: léelo en la cotización.</p>", sent.Body);
        Assert.Contains("<p>Conviértela en una orden de servicio para agendar y cobrar el trabajo.</p>", sent.Body);
        Assert.Contains($"<a href='{AppUrl}/quotes/{quote.Id}'>Abrir la cotización</a>", sent.Body);
        Assert.Contains(
            $"Te llega porque recibes un correo cada vez que un cliente responde una cotización de Plomer&#237;a &lt;Ruiz&gt; &amp; Hijos desde su enlace. Puedes dejar de recibirlos en <a href='{AppUrl}/profile'>Mi perfil</a>.",
            sent.Body);
        Assert.DoesNotContain("jueves", sent.Body);
        Assert.DoesNotContain("5522107765", sent.Body + sent.Subject);
    }

    [Fact]
    public async Task TheEmailOfARejectedQuote_ShouldOfferToCorrectIt_AndWithoutAMessageNorLinksSaySo()
    {
        await using var fixture = Fixture();
        var (quote, trade, owner) = await AnsweredQuoteAsync(fixture, accepted: false);

        var sent = Assert.Single(await SendReplyAsync(fixture, trade, quote, owner, appUrl: null));

        Assert.Equal("Cotización rechazada en Plomería Ruiz: COT-000007 por $1,160.00", sent.Subject);
        Assert.Contains("<h2>Te rechazaron una cotización</h2>", sent.Body);
        Assert.Contains("rechazó desde su enlace la cotización <strong>COT-000007</strong>", sent.Body);
        Assert.Contains("<p>Si quieres, corrígela y mándale otra versión.</p>", sent.Body);
        Assert.Contains("<p>Ábrela en Cotizaciones de TienditApp.</p>", sent.Body);
        Assert.Contains("Puedes dejar de recibirlos en Mi perfil.", sent.Body);
        Assert.DoesNotContain("mensaje", sent.Body);
        Assert.DoesNotContain("Conviértela", sent.Body);
        Assert.DoesNotContain("href", sent.Body);
    }

    [Fact]
    public async Task TheEmailOfAnAcceptedQuote_ShouldNotOfferTheOrder_WhenItsBusinessHasAppointmentsButNoServices()
    {
        await using var fixture = Fixture();
        // A shop with the agenda: it quotes its visits, but an accepted quote does not become a service order there.
        var shop = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail | TenantCapability.Appointments, slug: "tienda-agenda");
        var owner = await MemberAsync(fixture, shop, "dueño", TenantUserRole.Owner);
        var quote = await SentQuoteAsync(fixture, shop, 1);
        quote.ReplyFromLink(true, Today, null, Now.AddMinutes(-1));
        await fixture.Context.SaveChangesAsync();

        var sent = Assert.Single(await SendReplyAsync(fixture, shop, quote, owner));

        Assert.StartsWith("Cotización aceptada en ", sent.Subject);
        Assert.DoesNotContain("Conviértela", sent.Body);
    }

    [Fact]
    public void TheEmailOfAnAcceptedQuote_ShouldNotOfferTheOrder_WhereTheBusinessHasNoServices_AndShouldNameItsVersion()
    {
        var email = QuoteReplyEmail.Compose(
            "Plomería", "Valeria", "COT-000007 v2", accepted: true, 990m, leftAMessage: false, mayConvert: false, Guid.NewGuid(), AppUrl);

        Assert.Equal("Cotización aceptada en Plomería: COT-000007 v2 por $990.00", email.Subject);
        Assert.DoesNotContain("Conviértela", email.HtmlBody);
        Assert.DoesNotContain("mensaje", email.HtmlBody);
    }

    [Fact]
    public async Task TheEmailOfAnAnswer_ShouldNotGo_WhenTheMemberStoppedGettingThem_TheQuoteBecameItsOrder_OrItIsOfAnotherBusiness()
    {
        await using var fixture = Fixture();
        var (quote, trade, owner) = await AnsweredQuoteAsync(fixture, accepted: true);
        var admin = await MemberAsync(fixture, trade, "admin", TenantUserRole.Admin, chooseQuotes: true);
        fixture.TenantService.SetTenant(trade.Id);
        (await fixture.Context.TenantUsers.SingleAsync(m => m.UserId == admin)).ChooseQuoteReplyEmails(false);
        await fixture.Context.SaveChangesAsync();
        var other = await SalonAsync(fixture, "otra", "Estética Otra");

        Assert.Empty(await SendReplyAsync(fixture, trade, quote, admin));
        Assert.Empty(await SendReplyAsync(fixture, other, quote, owner));
        Assert.Single(await SendReplyAsync(fixture, trade, quote, owner));
        // It became its order while the email waited in the outbox.
        quote.ConvertToOrder(Guid.NewGuid(), 0m, Now);
        await fixture.Context.SaveChangesAsync();
        Assert.Empty(await SendReplyAsync(fixture, trade, quote, owner));
    }

    [Fact]
    public async Task EachPerson_ShouldChooseTheAnswersToQuotesForThemselves_ApartFromTheAgendas_InTheBusinessTheyAreIn()
    {
        await using var fixture = Fixture();
        var (trade, _) = await TradeAsync(fixture, "elige", "Plomería Elige");
        var owner = await MemberAsync(fixture, trade, "dueño", TenantUserRole.Owner);
        var admin = await MemberAsync(fixture, trade, "admin", TenantUserRole.Admin, emailConfirmed: false);
        var other = await SalonAsync(fixture, "otra", "Estética Otra");
        fixture.Context.TenantUsers.Add(Membership(other, owner, TenantUserRole.Staff));
        await fixture.Context.SaveChangesAsync();
        fixture.TenantService.SetTenant(trade.Id);

        var asOwner = QuoteHandler(fixture, owner);
        var asAdmin = QuoteHandler(fixture, admin);
        var before = (await asOwner.Handle(new GetQuoteReplyEmailsQuery(), CancellationToken.None)).Value;
        var adminBefore = (await asAdmin.Handle(new GetQuoteReplyEmailsQuery(), CancellationToken.None)).Value;
        var off = (await asOwner.Handle(new UpdateQuoteReplyEmailsCommand(false), CancellationToken.None)).Value;
        var adminOn = (await asAdmin.Handle(new UpdateQuoteReplyEmailsCommand(true), CancellationToken.None)).Value;

        Assert.Equal((true, $"dueño.{trade.Slug}@demo.com", true), (before.Enabled, before.Email, before.EmailConfirmed));
        Assert.Equal((false, false), (adminBefore.Enabled, adminBefore.EmailConfirmed));
        Assert.False(off.Enabled);
        Assert.True(adminOn.Enabled);
        var memberships = await fixture.Context.TenantUsers.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var ownerHere = memberships.Single(m => m.UserId == owner && m.TenantId == trade.Id);
        Assert.Equal(false, ownerHere.QuoteReplyEmails);
        // The agenda's notices stay as they were: the owner still gets them.
        Assert.Equal(((bool?)null, true), (ownerHere.RequestEmails, ownerHere.GetsRequestEmails));
        Assert.Null(memberships.Single(m => m.UserId == owner && m.TenantId == other.Id).QuoteReplyEmails);
        Assert.Equal(true, memberships.Single(m => m.UserId == admin).QuoteReplyEmails);

        // Whoever is not on the team (the platform's admin acting in it) has nothing to choose.
        var stranger = QuoteHandler(fixture, Guid.NewGuid());
        Assert.Equal(RequestEmailsHandler.NotAMemberMessage, (await stranger.Handle(new GetQuoteReplyEmailsQuery(), CancellationToken.None)).Error);
        Assert.Equal(RequestEmailsHandler.NotAMemberMessage, (await stranger.Handle(new UpdateQuoteReplyEmailsCommand(true), CancellationToken.None)).Error);
    }

    [Fact]
    public async Task TheScreensOfTheQuotes_ShouldHearWhichQuoteWasAnsweredAndHow_AndNotWhatTheCustomerWrote()
    {
        await using var fixture = Fixture();
        var (quote, trade, _) = await AnsweredQuoteAsync(fixture, accepted: true, response: "Puedo el sábado");
        fixture.TenantService.SetTenant(trade.Id);
        var screens = new QuoteScreens();

        await QuoteNotifications(fixture, screens)
            .Handle(new QuoteAnsweredFromLinkEvent(quote.Id, trade.Id, Accepted: true), CancellationToken.None);
        // One of another business is never told to this one.
        await QuoteNotifications(fixture, screens)
            .Handle(new QuoteAnsweredFromLinkEvent(quote.Id, Guid.NewGuid(), Accepted: false), CancellationToken.None);
        // A screen that fails does not fail the answer: the email is the notice that stays.
        await QuoteNotifications(fixture, new QuoteScreens(fail: true))
            .Handle(new QuoteAnsweredFromLinkEvent(quote.Id, trade.Id, Accepted: true), CancellationToken.None);

        var (tenantId, notice) = Assert.Single(screens.Answered);
        Assert.Equal(trade.Id, tenantId);
        Assert.Equal(new QuoteAnsweredNotice(quote.Id, quote.Number, quote.Revision, true), notice);
    }

    private static QuoteReplyEmailsHandler QuoteHandler(SalesTestFixture fixture, Guid userId)
        => new(fixture.Context, fixture.TenantService, new SalesCurrentUser { UserId = userId.ToString() });

    private static QuoteAnswerRealtimeNotifications QuoteNotifications(SalesTestFixture fixture, IQuoteNotificationService screens)
        => new(fixture.Context, screens, NullLogger<QuoteAnswerRealtimeNotifications>.Instance);

    /// <summary>What the live screens of the quotes heard (Fase 29R).</summary>
    private sealed class QuoteScreens(bool fail = false) : IQuoteNotificationService
    {
        public List<(Guid TenantId, QuoteAnsweredNotice Notice)> Answered { get; } = [];

        public Task NotifyQuoteAnswered(Guid tenantId, QuoteAnsweredNotice notice)
        {
            if (fail)
                throw new InvalidOperationException("SignalR");
            Answered.Add((tenantId, notice));
            return Task.CompletedTask;
        }
    }

    private static AppointmentCancellationRealtimeNotifications Notifications(SalesTestFixture fixture, IAppointmentNotificationService screens)
        => new(fixture.Context, screens, NullLogger<AppointmentCancellationRealtimeNotifications>.Instance);

    /// <summary>A request online its customer cancelled <paramref name="cancelledAt"/>, still waiting for an answer then.</summary>
    private static async Task<Appointment> CancelledAsync(
        SalesTestFixture fixture, Tenant tenant, DateTimeOffset cancelledAt, DateTimeOffset? startAt = null, bool fromTeam = false)
    {
        var request = await RequestAsync(fixture, tenant, cancelledAt.AddMinutes(-30), startAt, fromTeam: fromTeam);
        request.CancelByCustomer(cancelledAt);
        await fixture.Context.SaveChangesAsync();
        return request;
    }

    /// <summary>A quote of «Cambio de calentador» for $1,160 (folio <paramref name="number"/>), sent <paramref name="sentAt"/>.</summary>
    private static async Task<Quote> SentQuoteAsync(
        SalesTestFixture fixture, Tenant tenant, long number, DateTimeOffset? sentAt = null, DateOnly? today = null)
    {
        var at = sentAt ?? Now.AddHours(-2);
        var quote = Quote.Start(tenant.Id, Folios.QuoteNumber(number), new QuoteCustomer(null, "Valeria Ruiz", "+525522107765", null),
            null, true, 15, at);
        quote.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 1160m, VatRate.General)], at);
        quote.Send(today ?? Today, new string((char)('a' + number), 43), null, at);
        fixture.Context.Quotes.Add(quote);
        await fixture.Context.SaveChangesAsync();
        return quote;
    }

    /// <summary>The plumber «Plomería Ruiz» (or <paramref name="business"/>), its owner, and a quote its customer answered a minute ago from its link.</summary>
    private static async Task<(Quote Quote, Tenant Trade, Guid Owner)> AnsweredQuoteAsync(
        SalesTestFixture fixture, bool accepted, string business = "Plomería Ruiz", string? customer = null, string? response = null)
    {
        var (trade, _) = await TradeAsync(fixture, $"plomero{Guid.NewGuid():N}"[..16], business);
        var owner = await MemberAsync(fixture, trade, "dueño", TenantUserRole.Owner);
        var quote = Quote.Start(trade.Id, Folios.QuoteNumber(7), new QuoteCustomer(null, customer ?? "Valeria Ruiz", "+525522107765", null),
            null, true, 15, Now.AddHours(-2));
        quote.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 1160m, VatRate.General)], Now.AddHours(-2));
        quote.Send(Today, new string('q', 43), null, Now.AddHours(-2));
        quote.ReplyFromLink(accepted, Today, response, Now.AddMinutes(-1));
        fixture.Context.Quotes.Add(quote);
        await fixture.Context.SaveChangesAsync();
        return (quote, trade, owner);
    }

    /// <summary>The emails the outbox queued after the customer cancelled <paramref name="appointment"/>.</summary>
    private static async Task<List<(AppointmentCancellationEmailEvent Event, Guid? TenantId)>> QueueCancellationAsync(
        SalesTestFixture fixture, Tenant tenant, Appointment appointment, bool wasRequest)
    {
        fixture.TenantService.SetTenant(tenant.Id);
        var outbox = new Outbox<AppointmentCancellationEmailEvent>();
        await new AppointmentCancelledEmailsHandler(fixture.Context, new MembersAccess(fixture.Context), outbox, fixture.Clock)
            .HandleAsync(
                new AppointmentCancelledByCustomerEvent(appointment.Id, tenant.Id, appointment.IsHomeVisit, appointment.FromTeam, wasRequest),
                Context(tenant),
                CancellationToken.None);
        return outbox.Added;
    }

    /// <summary>What the email of <paramref name="appointment"/>'s cancellation to <paramref name="userId"/> sent, run in <paramref name="tenant"/>.</summary>
    private static async Task<List<(string To, string Subject, string Body)>> SendCancellationAsync(
        SalesTestFixture fixture, Tenant tenant, Appointment appointment, Guid userId, bool wasRequest, string? appUrl = AppUrl)
    {
        fixture.TenantService.SetTenant(tenant.Id);
        var emails = new Emails();
        await new AppointmentCancellationEmailHandler(fixture.Context, new MembersAccess(fixture.Context), emails, new Links(appUrl), fixture.Clock)
            .HandleAsync(new AppointmentCancellationEmailEvent(appointment.Id, userId, wasRequest), Context(tenant), CancellationToken.None);
        return emails.Sent;
    }

    /// <summary>The emails the outbox queued after the customer answered <paramref name="quote"/> from its link.</summary>
    private static async Task<List<(QuoteReplyEmailEvent Event, Guid? TenantId)>> QueueReplyAsync(
        SalesTestFixture fixture, Tenant tenant, Quote quote, bool accepted)
    {
        fixture.TenantService.SetTenant(tenant.Id);
        var outbox = new Outbox<QuoteReplyEmailEvent>();
        await new QuoteAnsweredEmailsHandler(fixture.Context, new MembersAccess(fixture.Context), outbox, fixture.Clock)
            .HandleAsync(new QuoteAnsweredFromLinkEvent(quote.Id, tenant.Id, accepted), Context(tenant), CancellationToken.None);
        return outbox.Added;
    }

    /// <summary>What the email of the answer to <paramref name="quote"/> to <paramref name="userId"/> sent, run in <paramref name="tenant"/>.</summary>
    private static async Task<List<(string To, string Subject, string Body)>> SendReplyAsync(
        SalesTestFixture fixture, Tenant tenant, Quote quote, Guid userId, string? appUrl = AppUrl)
    {
        fixture.TenantService.SetTenant(tenant.Id);
        var emails = new Emails();
        await new QuoteReplyEmailHandler(fixture.Context, new MembersAccess(fixture.Context), emails, new Links(appUrl), fixture.Clock)
            .HandleAsync(new QuoteReplyEmailEvent(quote.Id, userId), Context(tenant), CancellationToken.None);
        return emails.Sent;
    }
}
