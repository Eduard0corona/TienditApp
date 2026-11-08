using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// What the platform watches of the requests marked as fake (Fase 30A, ADR 0081): the businesses that mark the most, with
/// their requests to compare, and the phones two or more businesses marked, by their last four digits.
/// </summary>
public class FakeRequestsWatchTests
{
    private static readonly DateTimeOffset Now = SalesTestFixture.Now;

    [Fact]
    public async Task ThePlatform_ShouldSeeTheBusinessesThatMarkTheMost_AndThePhonesSeveralMarked()
    {
        await using var fixture = SalesTestFixture.Create();
        var luna = await SalonAsync(fixture, "luna", "Estética Luna");
        var pepe = await SalonAsync(fixture, "pepe", "Barbería Don Pepe");
        var sol = await SalonAsync(fixture, "sol", "Spa Sol");
        // Luna marks three phones and takes one back, of six requests of its customers (and one of its team, never counted).
        await RequestAsync(fixture, luna, "+525522107765", marked: Now.AddDays(-2));
        await RequestAsync(fixture, luna, "+525511112222", marked: Now.AddDays(-1));
        await RequestAsync(fixture, luna, "+525533334444", marked: Now.AddDays(-3), takenBack: true);
        for (var n = 0; n < 3; n++)
            await RequestAsync(fixture, luna, $"+5255444455{n:00}");
        await RequestAsync(fixture, luna, "+525599990000", fromTeam: true);
        // Don Pepe marks the first of Luna's phones, written with the 1 WhatsApp adds, and the one Luna took back.
        await RequestAsync(fixture, pepe, "+5215522107765", marked: Now.AddDays(-5));
        await RequestAsync(fixture, pepe, "+525533334444", marked: Now.AddDays(-4));
        // Sol marked Luna's second phone more than 90 days ago: neither it nor that mark counts.
        await RequestAsync(fixture, sol, "+525511112222", marked: Now.AddDays(-91));

        var watch = await new GetFakeRequestsWatchQueryHandler(fixture.Context, fixture.Clock)
            .Handle(new GetFakeRequestsWatchQuery(), CancellationToken.None);

        Assert.Equal(
            [("Estética Luna", 2, 1, 6, Now.AddDays(-1)), ("Barbería Don Pepe", 2, 0, 2, Now.AddDays(-4))],
            watch.Businesses.Select(b => (b.Name, b.Marked, b.TakenBack, b.Requests, b.LastMarkedAt)));
        var phone = Assert.Single(watch.Phones);
        Assert.Equal(("7765", Now.AddDays(-2)), (phone.Ending, phone.LastMarkedAt));
        Assert.Equal(["Barbería Don Pepe", "Estética Luna"], phone.BusinessNames);
        Assert.Equal(GetFakeRequestsWatchQueryHandler.Top, watch.Top);
    }

    [Fact]
    public void OnlyThePlatformAdmin_ShouldAskForIt()
        => Assert.Single(typeof(GetFakeRequestsWatchQuery).GetCustomAttributes(
            typeof(FoodParkDigital.Application.Common.Security.RequiresPlatformAdminAttribute), false));

    private static async Task<Tenant> SalonAsync(SalesTestFixture fixture, string slug, string name)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments, slug: slug);
        tenant.Update(name, $"{slug}@demo.com", null, null);
        await fixture.Context.SaveChangesAsync();
        return tenant;
    }

    /// <summary>
    /// A request online of a customer, with its phone as the request stores it (normalized, +52 and ten digits), marked as fake
    /// at <paramref name="marked"/> if given.
    /// </summary>
    private static async Task RequestAsync(
        SalesTestFixture fixture, Tenant tenant, string phone, DateTimeOffset? marked = null, bool takenBack = false, bool fromTeam = false)
    {
        var startAt = Now.AddDays(2);
        var request = Appointment.RequestOnline(
            tenant.Id, "Valeria Ruiz", phone, Guid.NewGuid(), "Corte de cabello", startAt, startAt.AddHours(1), null,
            customerUserId: null, clientRequestId: Guid.NewGuid(), trackingToken: (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..Order.TrackingTokenLength], fromTeam: fromTeam);
        if (marked is { } at)
        {
            request.MarkFake(at);
            if (takenBack)
                request.UnmarkFake(at.AddHours(1));
        }
        fixture.Context.Appointments.Add(request);
        fixture.Context.Entry(request).Property(a => a.CreatedAt).CurrentValue = (marked ?? Now).AddDays(-1);
        await fixture.Context.SaveChangesAsync();
    }
}
