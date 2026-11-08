using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Quotes;
using FoodParkDigital.Domain.Quotes.Entities;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using static FoodParkDigital.Application.Tests.ExploreResponseTimeTests;
using static FoodParkDigital.Application.Tests.OrderConfirmTimeInExploreTests;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The order of Explorar with every time it counts (Fase 29W, ADR 0077): answering requests, sending quotes and confirming
/// orders, on one scale, within each group of every list.
/// </summary>
public class ExploreOrderTests
{
    [Fact]
    public void ARequestsTrade_ShouldCountAsItsSlowestStep_ABusinessWithOrdersToo_AsItsQuickest_AndOneWithoutTimes_InTheMiddle()
    {
        // Nothing yet: in the middle, as before.
        Assert.Equal(ResponseTimes.Order(null), ExploreOrder.Of(null, null, null));
        // Requests alone, or orders alone, by their own time.
        Assert.Equal(0, ExploreOrder.Of(ResponseTime.WithinAnHour, null, null));
        Assert.Equal(0, ExploreOrder.Of(null, null, OrderConfirmTime.WithinFiveMinutes));
        Assert.Equal(4, ExploreOrder.Of(null, null, OrderConfirmTime.Slower));
        // A trade that answers within an hour and quotes within three days goes as the slower of the two steps; one that takes a
        // week or more to quote goes last, though it answers at once.
        Assert.Equal(1, ExploreOrder.Of(ResponseTime.WithinAnHour, QuoteTime.WithinThreeDays, null));
        Assert.Equal(4, ExploreOrder.Of(ResponseTime.WithinAnHour, QuoteTime.Slower, null));
        // Its quotes alone, when its answers say nothing yet.
        Assert.Equal(1, ExploreOrder.Of(null, QuoteTime.WithinThreeDays, null));
        // One that takes requests and orders goes by the quicker: they are two ways of buying from it.
        Assert.Equal(0, ExploreOrder.Of(ResponseTime.Slower, null, OrderConfirmTime.WithinFiveMinutes));
        Assert.Equal(1, ExploreOrder.Of(ResponseTime.WithinThreeHours, null, OrderConfirmTime.Slower));
    }

    [Fact]
    public async Task TheWholeList_ShouldPutFirstTheQuicker_KitchensAndTradesWithTheSalons_WithinEachGroup()
    {
        await using var fixture = Fixture();
        // Says nothing yet: in the middle.
        await SalonAsync(fixture, "a-salon-nuevo", "A Salón Nuevo");
        // Confirms its orders after 40 minutes: last.
        var slowKitchen = await KitchenAsync(fixture, "b-cocina-lenta", "B Cocina Lenta");
        for (var n = 0; n < 3; n++)
            await OrderAsync(fixture, slowKitchen, answeredAfter: TimeSpan.FromMinutes(40));
        // Accepts each order as it arrives: first.
        var quickKitchen = await KitchenAsync(fixture, "c-cocina-rapida", "C Cocina Rápida");
        for (var n = 0; n < 3; n++)
            await OrderAsync(fixture, quickKitchen, automatically: true);
        // Answers its visits within an hour but quotes them two days after: as the slower step, with the second tramo.
        var (plumber, zone) = await TradeAsync(fixture, "d-plomeria", "D Plomería");
        for (var n = 0; n < 3; n++)
            await QuotedTradeVisitAsync(fixture, plumber, zone);
        // Answers within an hour: first, with the quick kitchen.
        var quickSalon = await SalonAsync(fixture, "e-salon-rapido", "E Salón Rápido");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, quickSalon, TimeSpan.FromMinutes(20));

        var everyone = await Handler(fixture).Handle(new GetPublicDirectoryQuery(), CancellationToken.None);

        Assert.Equal(
            ["c-cocina-rapida", "e-salon-rapido", "d-plomeria", "a-salon-nuevo", "b-cocina-lenta"],
            everyone.Businesses.Items.Select(b => b.Slug));
    }

    /// <summary>A visit requested online, accepted half an hour after it arrived, and quoted two days after it ended.</summary>
    private static async Task QuotedTradeVisitAsync(SalesTestFixture fixture, Tenant trade, DeliveryZone zone)
    {
        var end = Now.AddDays(-10);
        var visit = Appointment.RequestHomeVisit(
            trade.Id, "Valeria", NewCustomerPhone(), null, HomeVisitRules.DefaultVisitName, end.AddHours(-1), end,
            "Gotea el calentador", null, DeliveryAddress.ForVisit("72000", "Av. Juárez 123", null), zone,
            HomeVisitChargePolicy.Charged, null, Guid.NewGuid(), Token());
        visit.CreatedAt = end.AddDays(-2);
        visit.Accept(visit.CreatedAt.AddMinutes(30));
        fixture.Context.Appointments.Add(visit);
        var sentAt = end.AddDays(2);
        var quote = Quote.Start(trade.Id, Folios.QuoteNumber(Interlocked.Increment(ref _folio)),
            new QuoteCustomer(null, "Valeria", visit.CustomerPhone, null), visit.Id, true, 15, sentAt.AddHours(-1));
        quote.ReplaceLines([new QuoteLineInput(null, "Cambio de calentador", 1m, 1160m, VatRate.General)], sentAt.AddHours(-1));
        quote.Send(DateOnly.FromDateTime(sentAt.UtcDateTime), Token(), null, sentAt);
        fixture.Context.Quotes.Add(quote);
        await fixture.Context.SaveChangesAsync();
    }

    private static int _folio;

    private static string Token() => (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..43];
}
