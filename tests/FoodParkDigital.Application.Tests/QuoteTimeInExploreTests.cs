using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
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
    public async Task TheOwner_ShouldReadHowSoonItQuotes_TheSlowerToo_AndTheVisitPageWhatItsCardSays()
    {
        await using var fixture = Fixture();
        var (slow, zone) = await TradeAsync(fixture, "plomeria", "Plomería Hernández");
        for (var n = 0; n < 3; n++)
            await QuotedVisitAsync(fixture, slow, zone, TimeSpan.FromDays(9));
        var (quick, otherZone) = await TradeAsync(fixture, "electricista", "Electricista Ruiz");
        for (var n = 0; n < 3; n++)
            await QuotedVisitAsync(fixture, quick, otherZone, TimeSpan.FromHours(50));

        Assert.Equal(QuoteTime.Slower, (await ProfileAsync(fixture, slow)).QuoteTime);
        Assert.Equal(QuoteTime.WithinThreeDays, (await ProfileAsync(fixture, quick)).QuoteTime);
        var cards = await CardsAsync(fixture);
        Assert.Null(cards["plomeria"].QuoteTime);
        Assert.Equal(QuoteTime.WithinThreeDays, cards["electricista"].QuoteTime);
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
    private static async Task QuotedVisitAsync(
        SalesTestFixture fixture, Tenant trade, DeliveryZone zone, TimeSpan sentAfterTheEnd, int endedDaysAgo = 10,
        string? phone = null, bool fromTeam = false)
    {
        var end = Now.AddDays(-endedDaysAgo);
        var visit = Appointment.RequestHomeVisit(
            trade.Id, "Valeria", phone ?? NewCustomerPhone(), null, HomeVisitRules.DefaultVisitName, end.AddHours(-1), end,
            "Gotea el calentador", null, DeliveryAddress.ForVisit("72000", "Av. Juárez 123", null), zone,
            HomeVisitChargePolicy.Charged, null, Guid.NewGuid(), Token(), fromTeam: fromTeam);
        visit.CreatedAt = end.AddDays(-2);
        fixture.Context.Appointments.Add(visit);
        await SendQuoteAsync(fixture, trade, visit, end + sentAfterTheEnd);
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

    private static Task<PublicDirectorySnapshot> SnapshotAsync(SalesTestFixture fixture)
        => PublicDirectorySnapshot.BuildAsync(fixture.Context, fixture.Clock.UtcNow, CancellationToken.None);

    private static PublicDirectorySnapshot.Entry Entry(PublicDirectorySnapshot snapshot, Tenant tenant)
        => snapshot.Entries.Single(entry => entry.TenantId == tenant.Id);
}
