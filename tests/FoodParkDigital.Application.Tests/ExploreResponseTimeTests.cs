using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// How soon a business answers in Explorar (Fase 29J, ADR 0064): its card says within what it answered at least half of the
/// requests its customers made online in the last 90 days, from three and up to a day; those it let pass count as the
/// slowest. Within its group, the businesses that answer sooner go first, one without enough requests in the middle.
/// </summary>
public class ExploreResponseTimeTests
{
    private static readonly DateTimeOffset Now = SalesTestFixture.Now;
    private static readonly DayOfWeek[] EveryDay = Enum.GetValues<DayOfWeek>();

    [Fact]
    public async Task Card_ShouldSayWithinWhatItAnsweredAtLeastHalf_FromThreeRequests_UpToADay()
    {
        await using var fixture = SalesTestFixture.Create();
        // Two of three within an hour, one of them declined.
        var quick = await SalonAsync(fixture, "rapida", "Estética Rápida");
        await AnsweredAsync(fixture, quick, TimeSpan.FromMinutes(30));
        await AnsweredAsync(fixture, quick, TimeSpan.FromMinutes(55), declined: true);
        await AnsweredAsync(fixture, quick, TimeSpan.FromHours(5));
        // Two quick answers say nothing yet.
        var newer = await SalonAsync(fixture, "nueva", "Estética Nueva");
        await AnsweredAsync(fixture, newer, TimeSpan.FromMinutes(10));
        await AnsweredAsync(fixture, newer, TimeSpan.FromMinutes(10));
        // Two of three within three hours; the third it let pass, the slowest.
        var hours = await SalonAsync(fixture, "horas", "Estética Horas");
        await AnsweredAsync(fixture, hours, TimeSpan.FromHours(2));
        await AnsweredAsync(fixture, hours, TimeSpan.FromHours(3));
        await LapsedAsync(fixture, hours);
        // More than a day: the card says nothing.
        var slow = await SalonAsync(fixture, "lenta", "Estética Lenta");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, slow, TimeSpan.FromHours(30));
        // Most of them let pass: nothing either, though it answered one at once.
        var silent = await SalonAsync(fixture, "callada", "Estética Callada");
        await AnsweredAsync(fixture, silent, TimeSpan.FromMinutes(5));
        await LapsedAsync(fixture, silent);
        await LapsedAsync(fixture, silent);

        var found = await CardsAsync(fixture);

        Assert.Equal(ResponseTime.WithinAnHour, found["rapida"].ResponseTime);
        Assert.Null(found["nueva"].ResponseTime);
        Assert.Equal(ResponseTime.WithinThreeHours, found["horas"].ResponseTime);
        Assert.Null(found["lenta"].ResponseTime);
        Assert.Null(found["callada"].ResponseTime);
    }

    [Fact]
    public async Task Card_ShouldCountOnlyTheRequestsOnlineOfTheLast90Days_AnsweredOrLetPass_OfItsOwnBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var salon = await SalonAsync(fixture, "sol", "Estética Sol");
        var other = await SalonAsync(fixture, "luna", "Estética Luna");
        // One answer at once that counts, and the slow ones that would make it slower if they counted.
        await AnsweredAsync(fixture, salon, TimeSpan.FromMinutes(20));
        await AnsweredAsync(fixture, salon, TimeSpan.FromMinutes(20));
        await AnsweredAsync(fixture, salon, TimeSpan.FromHours(30), arrivedDaysAgo: ResponseTimes.WindowDays + 1);
        await AnsweredAsync(fixture, salon, TimeSpan.FromHours(30), arrivedDaysAgo: ResponseTimes.WindowDays + 5);
        // Still waiting, and cancelled by its customer before an answer: they do not count yet.
        await RequestAsync(fixture, salon, Now.AddHours(-2));
        var cancelled = await RequestAsync(fixture, salon, Now.AddHours(-3));
        cancelled.CancelByCustomer(Now.AddHours(-1));
        // Booked by the team: no request.
        var ofTheTeam = new Appointment(salon.Id, "Ana", "5522107765", "Corte", Now.AddDays(1), Now.AddDays(1).AddHours(1))
        {
            CreatedAt = Now.AddDays(-1)
        };
        fixture.Context.Appointments.Add(ofTheTeam);
        // The slow answers of another business are its own.
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, other, TimeSpan.FromHours(30));
        await fixture.Context.SaveChangesAsync();

        var found = await CardsAsync(fixture);

        // Two count: fewer than three say nothing.
        Assert.Null(found["sol"].ResponseTime);
        Assert.Null(found["luna"].ResponseTime);

        // A third answer within the last days, slow: still within an hour, two of three.
        await AnsweredAsync(fixture, salon, TimeSpan.FromHours(30), arrivedDaysAgo: ResponseTimes.WindowDays - 1);
        Assert.Equal(ResponseTime.WithinAnHour, (await CardsAsync(fixture))["sol"].ResponseTime);
    }

    [Fact]
    public async Task Explorar_ShouldPutFirstWhoAnswersSooner_WithinItsGroup_AndTheUnknownInTheMiddle()
    {
        await using var fixture = SalesTestFixture.Create();
        var slow = await SalonAsync(fixture, "a-lenta", "A Lenta");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, slow, TimeSpan.FromHours(30));
        await SalonAsync(fixture, "b-nueva", "B Nueva");
        var quick = await SalonAsync(fixture, "c-rapida", "C Rápida");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, quick, TimeSpan.FromMinutes(15));
        var day = await SalonAsync(fixture, "d-un-dia", "D Un Día");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, day, TimeSpan.FromHours(20));
        var twelve = await SalonAsync(fixture, "e-doce", "E Doce");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, twelve, TimeSpan.FromHours(10));
        // It stopped booking online: its menu (with a product) is open, a group after those that book, however soon it answered.
        var stopped = await SalonAsync(fixture, "f-sin-agenda", "F Sin Agenda", bookingOn: false);
        var shelf = await fixture.Context.Categories.IgnoreQueryFilters().SingleAsync(c => c.TenantId == stopped.Id);
        fixture.Context.Products.Add(new Product(stopped.Id, shelf.Id, "Shampoo", null, 90m, "MXN"));
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, stopped, TimeSpan.FromMinutes(5));

        var listed = await Handler(fixture).Handle(new GetPublicDirectoryQuery(), CancellationToken.None);

        Assert.Equal(
            ["c-rapida", "b-nueva", "e-doce", "d-un-dia", "a-lenta", "f-sin-agenda"],
            listed.Businesses.Items.Select(b => b.Slug));
        Assert.Equal(
            [ResponseTime.WithinAnHour, null, ResponseTime.WithinTwelveHours, ResponseTime.WithinADay, null, ResponseTime.WithinAnHour],
            listed.Businesses.Items.Select(b => b.ResponseTime));
    }

    /// <summary>A salon that books online every day, its menu published.</summary>
    private static async Task<Tenant> SalonAsync(SalesTestFixture fixture, string slug, string name, bool bookingOn = true)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments, slug: slug);
        tenant.Update(name, $"{slug}@demo.com", null, null);
        tenant.SetDirectoryProfile(BusinessCategory.Beauty, null);
        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineBooking(bookingOn, 1);
        settings.UpdateOnlineMenu(OnlineMenuMode.MenuOnly);
        settings.SetOpeningHours(OpeningHours.Create(EveryDay.Select(d => new OpeningInterval(d, TimeOnly.MinValue, new TimeOnly(23, 59)))));
        var category = new Category(tenant.Id, "Servicios", null);
        fixture.Context.Categories.Add(category);
        fixture.Context.Products.Add(new Product(tenant.Id, category.Id, "Corte", null, 150m, "MXN", unit: UnitType.Service));
        await fixture.Context.SaveChangesAsync();
        return tenant;
    }

    /// <summary>A request online that arrived <paramref name="arrivedAt"/> for two days from now, still waiting.</summary>
    private static async Task<Appointment> RequestAsync(SalesTestFixture fixture, Tenant tenant, DateTimeOffset arrivedAt, DateTimeOffset? startAt = null)
    {
        var start = startAt ?? Now.AddDays(2);
        var request = Appointment.RequestOnline(
            tenant.Id, "Valeria", "+525522107765", Guid.NewGuid(), "Corte", start, start.AddHours(1), null, null, Guid.NewGuid(),
            (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..43]);
        request.CreatedAt = arrivedAt;
        fixture.Context.Appointments.Add(request);
        await fixture.Context.SaveChangesAsync();
        return request;
    }

    /// <summary>A request the business accepted (or declined) after its customer waited <paramref name="waited"/>.</summary>
    private static async Task AnsweredAsync(
        SalesTestFixture fixture, Tenant tenant, TimeSpan waited, int arrivedDaysAgo = 3, bool declined = false)
    {
        var arrivedAt = Now.AddDays(-arrivedDaysAgo);
        var request = await RequestAsync(fixture, tenant, arrivedAt);
        if (declined)
            request.Decline(arrivedAt + waited);
        else
            request.Accept(arrivedAt + waited);
        await fixture.Context.SaveChangesAsync();
    }

    /// <summary>A request whose time began without an answer.</summary>
    private static Task LapsedAsync(SalesTestFixture fixture, Tenant tenant)
        => RequestAsync(fixture, tenant, Now.AddDays(-2), startAt: Now.AddHours(-1));

    private static GetPublicDirectoryQueryHandler Handler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.Clock, new PublicDirectoryCache(), new PublicProductIndexCache(), NullLogger<GetPublicDirectoryQueryHandler>.Instance);

    private static async Task<Dictionary<string, PublicDirectoryBusinessDto>> CardsAsync(SalesTestFixture fixture)
        => (await Handler(fixture).Handle(new GetPublicDirectoryQuery(), CancellationToken.None))
            .Businesses.Items.ToDictionary(b => b.Slug);
}
