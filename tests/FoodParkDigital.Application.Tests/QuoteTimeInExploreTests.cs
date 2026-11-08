using System.Linq.Expressions;
using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging.Abstractions;
using static FoodParkDigital.Application.Tests.ExploreResponseTimeTests;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// How soon a trade sends the quote after the visits its customers request online (Fase 29S, ADR 0073), and the order of
/// Explorar by how soon each business answers, now in every list of it.
/// </summary>
public class QuoteTimeInExploreTests
{
    [Fact]
    public async Task ACard_ShouldSayWithinWhatATradeSentHalfItsQuotes_AfterTheVisit_FromThreeVisits()
    {
        await using var fixture = Fixture();
        var (plumber, zone) = await TradeAsync(fixture, "plomeria", "Plomería Hernández");
        // Quoted at the visit itself, a day and a half after it, and two days after it.
        await QuotedVisitAsync(fixture, plumber, zone, TimeSpan.FromHours(-1));
        await QuotedVisitAsync(fixture, plumber, zone, TimeSpan.FromHours(36));
        await QuotedVisitAsync(fixture, plumber, zone, TimeSpan.FromDays(2));
        var (electrician, otherZone) = await TradeAsync(fixture, "electricista", "Electricista Ruiz");
        await QuotedVisitAsync(fixture, electrician, otherZone, TimeSpan.FromHours(1));
        await QuotedVisitAsync(fixture, electrician, otherZone, TimeSpan.FromHours(1));

        var cards = await CardsAsync(fixture);

        Assert.Equal(QuoteTime.WithinThreeDays, cards["plomeria"].QuoteTime);
        // Two visits quoted say nothing.
        Assert.Null(cards["electricista"].QuoteTime);
    }

    [Fact]
    public async Task OnlyTheVisitsRequestedOnlineByItsCustomers_ShouldCount_OncePerCustomer_InTheLast90Days()
    {
        await using var fixture = Fixture();
        var (plumber, zone) = await TradeAsync(fixture, "plomeria", "Plomería Hernández");
        // Three customers quoted after more than a week: slower than a week, which the card does not say.
        var phone = NewCustomerPhone();
        await QuotedVisitAsync(fixture, plumber, zone, TimeSpan.FromDays(8), phone: phone, endedDaysAgo: 30);
        await QuotedVisitAsync(fixture, plumber, zone, TimeSpan.FromDays(8));
        await QuotedVisitAsync(fixture, plumber, zone, TimeSpan.FromDays(8));
        // None of these counts: one of its own team, one quoted more than 90 days ago, an appointment that is not a visit, and
        // the visits of another business.
        await QuotedVisitAsync(fixture, plumber, zone, TimeSpan.FromHours(1), fromTeam: true);
        await QuotedVisitAsync(fixture, plumber, zone, TimeSpan.FromHours(1), endedDaysAgo: 100);
        await QuotedAppointmentAsync(fixture, plumber, TimeSpan.FromHours(1));
        var (other, otherZone) = await TradeAsync(fixture, "electricista", "Electricista Ruiz");
        for (var n = 0; n < 3; n++)
            await QuotedVisitAsync(fixture, other, otherZone, TimeSpan.FromHours(1));

        var before = await SnapshotAsync(fixture);
        Assert.Equal(QuoteTime.Slower, Entry(before, plumber).QuoteTime);
        Assert.Null(Entry(before, plumber).Business.QuoteTime);
        Assert.Equal(QuoteTime.WithinADay, Entry(before, other).QuoteTime);

        // The first customer asks again and is quoted at once: their last visit is the one that counts, so two of three are
        // still slower.
        await QuotedVisitAsync(fixture, plumber, zone, TimeSpan.FromHours(1), phone: phone, endedDaysAgo: 5);
        Assert.Equal(QuoteTime.Slower, Entry(await SnapshotAsync(fixture), plumber).QuoteTime);
        // A fourth customer quoted at once: two of four within a day is half.
        await QuotedVisitAsync(fixture, plumber, zone, TimeSpan.FromHours(1));
        Assert.Equal(QuoteTime.WithinADay, Entry(await SnapshotAsync(fixture), plumber).QuoteTime);
    }

    [Fact]
    public async Task OnlyVisitsThatTookPlace_ShouldCount_EachBusinessItsOwnCustomersAndQuotes()
    {
        await using var fixture = Fixture();
        var (plumber, zone) = await TradeAsync(fixture, "plomeria", "Plomería Hernández");
        var (electrician, otherZone) = await TradeAsync(fixture, "electricista", "Electricista Ruiz");
        var first = NewCustomerPhone();
        var second = NewCustomerPhone();
        await QuotedVisitAsync(fixture, plumber, zone, TimeSpan.FromHours(1), phone: first);
        await QuotedVisitAsync(fixture, plumber, zone, TimeSpan.FromHours(1), phone: second);
        // None of these counts: one its customer cancelled and one missed, though their quotes went out before; and one of
        // the plumber whose quote row belongs to another business.
        await QuotedVisitAsync(fixture, plumber, zone, TimeSpan.FromHours(-3), then: visit => visit.CancelByCustomer(Now.AddDays(-11)));
        await QuotedVisitAsync(fixture, plumber, zone, TimeSpan.FromHours(-3), then: visit =>
        {
            visit.Accept(Now.AddDays(-12));
            visit.ChangeStatus(AppointmentStatus.NoShow);
        });
        await QuotedVisitAsync(fixture, plumber, zone, TimeSpan.FromHours(1), quotedBy: electrician);
        // One quoted already whose visit is in two days: it counts once it took place.
        await QuotedVisitAsync(fixture, plumber, zone, TimeSpan.FromHours(-30), endedDaysAgo: -2);
        // The same two customers asked the electrician too, and were quoted later: each business counts its own.
        await QuotedVisitAsync(fixture, electrician, otherZone, TimeSpan.FromHours(1), phone: first, endedDaysAgo: 5);
        await QuotedVisitAsync(fixture, electrician, otherZone, TimeSpan.FromHours(1), phone: second, endedDaysAgo: 5);
        await QuotedVisitAsync(fixture, electrician, otherZone, TimeSpan.FromHours(1), endedDaysAgo: 5);

        var today = await SnapshotAsync(fixture);
        fixture.Clock.UtcNow = Now.AddDays(3);
        var inThreeDays = await SnapshotAsync(fixture);

        Assert.Null(Entry(today, plumber).QuoteTime);
        Assert.Equal(QuoteTime.WithinADay, Entry(today, electrician).QuoteTime);
        Assert.Equal(QuoteTime.WithinADay, Entry(inThreeDays, plumber).QuoteTime);
    }

    [Fact]
    public async Task AVisitAWeekPastItsEnd_WithoutItsQuoteSent_ShouldCountAmongTheSlowest_UnlessItsQuoteWasDiscarded_OrTheWorkCharged()
    {
        await using var fixture = Fixture();
        // Quoted five customers at once. Each of these counts, a week past its end: a quote started and never sent, a visit
        // that left nothing, one charged for the visit alone (charging it is the step before quoting), one whose work was
        // charged but has a quote started, one charged in a sale later cancelled, and a new quote started after the first
        // was discarded. Five of eleven within a day: with any of them less, half would be.
        var (drafted, zone) = await TradeAsync(fixture, "borrador", "Plomería Borrador");
        for (var n = 0; n < 5; n++)
            await QuotedVisitAsync(fixture, drafted, zone, TimeSpan.FromHours(1));
        foreach (var left in new[] { Left.Draft, Left.Nothing, Left.VisitCharged, Left.WorkChargedWithADraft, Left.WorkChargedThenCancelled, Left.DiscardedThenADraft })
            await VisitLeftAsync(fixture, drafted, zone, left);
        // Quoted two customers at once. None of these counts: one whose work was charged at the visit, one whose quote was
        // discarded, one six days past its end, one past the 90 days, and a request nobody accepted (it counts among the
        // slowest to answer, and never took place). With any of them more, two of three would be within a day.
        var (solved, otherZone) = await TradeAsync(fixture, "resuelto", "Plomería Resuelto");
        await QuotedVisitAsync(fixture, solved, otherZone, TimeSpan.FromHours(1));
        await QuotedVisitAsync(fixture, solved, otherZone, TimeSpan.FromHours(1));
        await VisitLeftAsync(fixture, solved, otherZone, Left.WorkCharged);
        await VisitLeftAsync(fixture, solved, otherZone, Left.Discarded);
        await VisitLeftAsync(fixture, solved, otherZone, Left.Nothing, endedDaysAgo: 6);
        await VisitLeftAsync(fixture, solved, otherZone, Left.Nothing, endedDaysAgo: 100);
        await VisitLeftAsync(fixture, solved, otherZone, Left.Nothing, accepted: false);

        var today = await SnapshotAsync(fixture);
        Assert.Equal(QuoteTime.Slower, Entry(today, drafted).QuoteTime);
        Assert.Null(Entry(today, solved).QuoteTime);

        // Two days later the one six days past is a week past too: two of three within a day.
        fixture.Clock.UtcNow = Now.AddDays(2);
        Assert.Equal(QuoteTime.WithinADay, Entry(await SnapshotAsync(fixture), solved).QuoteTime);
    }

    [Fact]
    public async Task ACustomer_ShouldCountOnce_ByTheVisitDecidedLast_ItsQuoteSentOrItsWeekPast()
    {
        await using var fixture = Fixture();
        // Two customers whose visit of 30 days ago was never quoted (a week past it 23 days ago), and whose later visit was
        // quoted an hour after it ended, 25 days ago: the one never quoted was decided last. With one other quoted at once,
        // one of three within a day; by their last visit, or counting both, it would be at least half.
        var (late, zone) = await TradeAsync(fixture, "tarde", "Plomería Tarde");
        await QuotedVisitAsync(fixture, late, zone, TimeSpan.FromHours(1));
        foreach (var phone in new[] { "5532100001", "5532100002" })
        {
            await VisitLeftAsync(fixture, late, zone, Left.Nothing, endedDaysAgo: 30, phone: phone);
            await QuotedVisitAsync(fixture, late, zone, TimeSpan.FromHours(1), endedDaysAgo: 25, phone: phone);
        }
        // Two customers whose visit of 30 days ago was never quoted, and whose visit of 21 days ago was quoted an hour after it
        // ended: the quote was decided last. With one other never quoted, two of three within a day; with the slow ones first,
        // or counting both, it would be less than half.
        var (recovered, otherZone) = await TradeAsync(fixture, "al-dia", "Plomería Al Día");
        await VisitLeftAsync(fixture, recovered, otherZone, Left.Nothing);
        foreach (var phone in new[] { "5532100001", "5532100002" })
        {
            await VisitLeftAsync(fixture, recovered, otherZone, Left.Nothing, endedDaysAgo: 30, phone: phone);
            await QuotedVisitAsync(fixture, recovered, otherZone, TimeSpan.FromHours(1), endedDaysAgo: 21, phone: phone);
        }

        var today = await SnapshotAsync(fixture);

        Assert.Equal(QuoteTime.Slower, Entry(today, late).QuoteTime);
        Assert.Equal(QuoteTime.WithinADay, Entry(today, recovered).QuoteTime);
    }

    [Fact]
    public async Task TheLimits_ShouldLeaveOut_AWaitOfExactlyADay()
    {
        await using var fixture = Fixture();
        var (carpenter, zone) = await TradeAsync(fixture, "carpinteria", "Carpintería Don José");
        // Within a day, exactly a day, exactly three days, and more than a week: half is within three days, not one.
        foreach (var hours in new[] { 23, 24, 72, 200 })
            await QuotedVisitAsync(fixture, carpenter, zone, TimeSpan.FromHours(hours));

        Assert.Equal(QuoteTime.WithinThreeDays, Entry(await SnapshotAsync(fixture), carpenter).QuoteTime);
    }

    [Fact]
    public async Task ASnapshotThatCouldNotReadTheQuotes_ShouldNotTellTheOwnerTheyAreNotCountedYet()
    {
        await using var fixture = SalesTestFixture.Create(null, new FailingQuoteTimes());
        fixture.Clock.UtcNow = Now;
        var (plumber, zone) = await TradeAsync(fixture, "plomeria", "Plomería Hernández");
        for (var n = 0; n < 3; n++)
            await QuotedVisitAsync(fixture, plumber, zone, TimeSpan.FromHours(1));

        var profile = await ProfileAsync(fixture, plumber);

        Assert.Equal((true, false, (QuoteTime?)null), (profile.CountsRequests, profile.CountsQuotes, profile.QuoteTime));
        Assert.Null((await CardsAsync(fixture))["plomeria"].QuoteTime);
    }

    [Fact]
    public async Task TheOwner_ShouldReadHowSoonItQuotes_TheSlowerToo_AndTheVisitPageWhatItsCardSays()
    {
        await using var fixture = Fixture();
        var (slow, zone) = await TradeAsync(fixture, "plomeria", "Plomería Hernández");
        for (var n = 0; n < 3; n++)
            await QuotedVisitAsync(fixture, slow, zone, TimeSpan.FromDays(9));
        var (quick, otherZone) = await TradeAsync(fixture, "electricista", "Electricista Ruiz");
        for (var n = 0; n < 3; n++)
            await QuotedVisitAsync(fixture, quick, otherZone, TimeSpan.FromHours(50));

        Assert.Equal((true, QuoteTime.Slower), ((await ProfileAsync(fixture, slow)).CountsQuotes, (await ProfileAsync(fixture, slow)).QuoteTime));
        Assert.Equal(QuoteTime.WithinThreeDays, (await ProfileAsync(fixture, quick)).QuoteTime);
        var cards = await CardsAsync(fixture);
        Assert.Null(cards["plomeria"].QuoteTime);
        Assert.Equal(QuoteTime.WithinThreeDays, cards["electricista"].QuoteTime);
        // Its visit page says what its card says: nothing of a week or more.
        Assert.Null((await VisitAsync(fixture, "plomeria"))!.QuoteTime);
        Assert.Equal(QuoteTime.WithinThreeDays, (await VisitAsync(fixture, "electricista"))!.QuoteTime);
    }

    [Fact]
    public async Task TheWholeListAndTheServicesOfASearch_ShouldPutFirstWhoAnswersSooner_WithinEachGroup()
    {
        await using var fixture = Fixture();
        var slow = await SalonAsync(fixture, "a-lenta", "A Lenta");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, slow, TimeSpan.FromHours(30));
        await SalonAsync(fixture, "b-nueva", "B Nueva");
        var quick = await SalonAsync(fixture, "c-rapida", "C Rápida");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, quick, TimeSpan.FromMinutes(15));

        var everyone = await Handler(fixture).Handle(new GetPublicDirectoryQuery(), CancellationToken.None);
        var search = await Handler(fixture).Handle(new GetPublicDirectoryQuery(Search: "corte"), CancellationToken.None);

        Assert.Equal(["c-rapida", "b-nueva", "a-lenta"], everyone.Businesses.Items.Select(b => b.Slug));
        // The search finds their «Corte», not them: the services follow the same order.
        Assert.Equal(["c-rapida", "b-nueva", "a-lenta"], search.Services.Select(service => service.BusinessSlug));
    }

    private static int _folio;

    private static string Token() => (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..43];

    /// <summary>
    /// A visit a customer requested online that ended <paramref name="endedDaysAgo"/>, and its quote sent
    /// <paramref name="sentAfterTheEnd"/> after it ended.
    /// </summary>
    /// <param name="then">What became of the visit after its quote went out.</param>
    /// <param name="quotedBy">The business of the quote row, the trade's own unless said.</param>
    private static async Task QuotedVisitAsync(
        SalesTestFixture fixture, Tenant trade, DeliveryZone zone, TimeSpan sentAfterTheEnd, int endedDaysAgo = 10,
        string? phone = null, bool fromTeam = false, Action<Appointment>? then = null, Tenant? quotedBy = null)
    {
        var end = Now.AddDays(-endedDaysAgo);
        var visit = Appointment.RequestHomeVisit(
            trade.Id, "Valeria", phone ?? NewCustomerPhone(), null, HomeVisitRules.DefaultVisitName, end.AddHours(-1), end,
            "Gotea el calentador", null, DeliveryAddress.ForVisit("72000", "Av. Juárez 123", null), zone,
            HomeVisitChargePolicy.Charged, null, Guid.NewGuid(), Token(), fromTeam: fromTeam);
        visit.CreatedAt = end.AddDays(-2);
        then?.Invoke(visit);
        fixture.Context.Appointments.Add(visit);
        await SendQuoteAsync(fixture, quotedBy ?? trade, visit, end + sentAfterTheEnd);
    }

    /// <summary>What came of a visit without its quote sent (Fase 29W).</summary>
    private enum Left
    {
        Nothing,
        Draft,
        Discarded,
        DiscardedThenADraft,
        /// <summary>Charged in a sale of the visit's own service alone.</summary>
        VisitCharged,
        /// <summary>Charged in a sale with something else than the visit's service: the work was done at the visit.</summary>
        WorkCharged,
        WorkChargedWithADraft,
        WorkChargedThenCancelled
    }

    /// <summary>
    /// A visit a customer requested online that ended <paramref name="endedDaysAgo"/>, accepted half an hour after it arrived
    /// unless said, without its quote sent.
    /// </summary>
    private static async Task VisitLeftAsync(
        SalesTestFixture fixture, Tenant trade, DeliveryZone zone, Left left, int endedDaysAgo = 8, bool accepted = true,
        string? phone = null)
    {
        var end = Now.AddDays(-endedDaysAgo);
        var visitService = Guid.NewGuid();
        var visit = Appointment.RequestHomeVisit(
            trade.Id, "Valeria", phone ?? NewCustomerPhone(), visitService, HomeVisitRules.DefaultVisitName, end.AddHours(-1), end,
            "Gotea el calentador", null, DeliveryAddress.ForVisit("72000", "Av. Juárez 123", null), zone,
            HomeVisitChargePolicy.Charged, null, Guid.NewGuid(), Token());
        visit.CreatedAt = end.AddDays(-2);
        if (accepted)
            visit.Accept(visit.CreatedAt.AddMinutes(30));
        if (left is Left.VisitCharged or Left.WorkCharged or Left.WorkChargedWithADraft or Left.WorkChargedThenCancelled)
        {
            var sale = new Order(trade.Id, null, null, true, $"ORD-{Interlocked.Increment(ref _folio):000000}", BusinessType.Service,
                OrderType.Service, "Valeria", null, null, null);
            sale.AddItem(visitService, HomeVisitRules.DefaultVisitName, 1m, 200m);
            if (left != Left.VisitCharged)
                sale.AddItem(Guid.NewGuid(), "Cambio de empaque", 1m, 350m);
            if (left == Left.WorkChargedThenCancelled)
                sale.Cancel(new ServiceOrderStateMachine(), "Ana");
            fixture.Context.Orders.Add(sale);
            visit.RecordSale(sale.Id);
        }

        fixture.Context.Appointments.Add(visit);
        if (left is Left.Draft or Left.Discarded or Left.DiscardedThenADraft or Left.WorkChargedWithADraft)
        {
            var quote = Quote.Start(trade.Id, Folios.QuoteNumber(Interlocked.Increment(ref _folio)),
                new QuoteCustomer(null, "Valeria", visit.CustomerPhone, null), visit.Id, true, 15, end.AddHours(1));
            if (left is Left.Discarded or Left.DiscardedThenADraft)
                quote.Discard(end.AddHours(2));
            fixture.Context.Quotes.Add(quote);
            if (left == Left.DiscardedThenADraft)
                fixture.Context.Quotes.Add(Quote.Start(trade.Id, Folios.QuoteNumber(Interlocked.Increment(ref _folio)),
                    new QuoteCustomer(null, "Valeria", visit.CustomerPhone, null), visit.Id, true, 15, end.AddHours(3)));
        }

        await fixture.Context.SaveChangesAsync();
    }

    /// <summary>An appointment booked online, not a visit, quoted <paramref name="sentAfterTheEnd"/> after it ended.</summary>
    private static async Task QuotedAppointmentAsync(SalesTestFixture fixture, Tenant tenant, TimeSpan sentAfterTheEnd)
    {
        var end = Now.AddDays(-10);
        var appointment = Appointment.RequestOnline(
            tenant.Id, "Valeria", NewCustomerPhone(), Guid.NewGuid(), "Revisión", end.AddHours(-1), end, null, null, Guid.NewGuid(), Token());
        appointment.CreatedAt = end.AddDays(-2);
        fixture.Context.Appointments.Add(appointment);
        await SendQuoteAsync(fixture, tenant, appointment, end + sentAfterTheEnd);
    }

    private static async Task SendQuoteAsync(SalesTestFixture fixture, Tenant tenant, Appointment appointment, DateTimeOffset sentAt)
    {
        var quote = Quote.Start(tenant.Id, Folios.QuoteNumber(Interlocked.Increment(ref _folio)),
            new QuoteCustomer(null, "Valeria", appointment.CustomerPhone, null), appointment.Id, true, 15, sentAt.AddHours(-1));
        quote.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 1160m, VatRate.General)], sentAt.AddHours(-1));
        quote.Send(DateOnly.FromDateTime(sentAt.UtcDateTime), Token(), null, sentAt);
        fixture.Context.Quotes.Add(quote);
        await fixture.Context.SaveChangesAsync();
    }

    private static Task<PublicHomeVisitDto?> VisitAsync(SalesTestFixture fixture, string slug)
        => new GetPublicHomeVisitQueryHandler(
                fixture.Context, fixture.TenantService, fixture.Clock, new PublicDirectoryCache(),
                NullLogger<GetPublicHomeVisitQueryHandler>.Instance)
            .Handle(new GetPublicHomeVisitQuery(slug), CancellationToken.None);

    private static async Task<DirectoryProfileDto> ProfileAsync(SalesTestFixture fixture, Tenant tenant)
    {
        fixture.TenantService.SetTenant(tenant.Id);
        var profile = await new GetDirectoryProfileQueryHandler(
                fixture.Context, fixture.TenantService, fixture.Clock, new PublicDirectoryCache(),
                NullLogger<GetDirectoryProfileQueryHandler>.Instance)
            .Handle(new GetDirectoryProfileQuery(), CancellationToken.None);
        Assert.True(profile.IsSuccess, profile.Error);
        return profile.Value!;
    }

    /// <summary>The read of the quotes fails, as a database that does not answer would; every other read works.</summary>
    private sealed class FailingQuoteTimes : IQueryExpressionInterceptor
    {
        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
            => new ReadsQuotes().Finds(queryExpression)
                ? throw new InvalidOperationException("La base no responde.")
                : queryExpression;

        private sealed class ReadsQuotes : ExpressionVisitor
        {
            private bool _found;

            public bool Finds(Expression expression)
            {
                Visit(expression);
                return _found;
            }

            public override Expression? Visit(Expression? node)
            {
                if (node is QueryRootExpression root && root.ElementType == typeof(Quote))
                    _found = true;
                return base.Visit(node);
            }
        }
    }

    private static Task<PublicDirectorySnapshot> SnapshotAsync(SalesTestFixture fixture)
        => PublicDirectorySnapshot.BuildAsync(fixture.Context, fixture.Clock.UtcNow, CancellationToken.None);

    private static PublicDirectorySnapshot.Entry Entry(PublicDirectorySnapshot snapshot, Tenant tenant)
        => snapshot.Entries.Single(entry => entry.TenantId == tenant.Id);
}
