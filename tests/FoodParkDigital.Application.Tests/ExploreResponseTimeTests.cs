using FoodParkDigital.Application.Appointments;
using FoodParkDigital.Application.Appointments.Commands;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.Enums;
using FoodParkDigital.Domain.Appointments.HomeVisits;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Delivery;
using FoodParkDigital.Domain.Orders.HomeDelivery;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// How soon a business answers in Explorar (Fase 29J, ADR 0064): its card says within what it answered at least half of the
/// requests its customers made online in the last 90 days, from three and less than a day. The slowest are those answered
/// once their time began or let pass, and those that waited a day without an answer, since the first answer kept. In a category, the
/// businesses of each group that answer sooner go first, one without enough requests in the middle.
/// </summary>
public class ExploreResponseTimeTests
{
    // Well past Fase 29J and its first 90 days, so the window is the one that cuts.
    internal static readonly DateTimeOffset Now = new(2027, 3, 1, 17, 0, 0, TimeSpan.Zero);
    private static readonly DayOfWeek[] EveryDay = Enum.GetValues<DayOfWeek>();

    [Fact]
    public async Task Card_ShouldSayWithinWhatItAnsweredAtLeastHalf_FromThreeRequests_UnderADay()
    {
        await using var fixture = Fixture();
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
        await AnsweredAsync(fixture, hours, TimeSpan.FromMinutes(150));
        await LapsedAsync(fixture, hours);
        // A day or more: the card says nothing.
        var slow = await SalonAsync(fixture, "lenta", "Estética Lenta");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, slow, TimeSpan.FromHours(24));
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
    public async Task TheSlowest_ShouldBeWhatWasAnsweredOnceItsTimeBegan_OrWaitedADayUnanswered()
    {
        await using var fixture = Fixture();
        // Answered within minutes, but once its time began: as if let pass.
        var late = await SalonAsync(fixture, "tarde", "Estética Tarde");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, late, TimeSpan.FromMinutes(20), startsAfter: TimeSpan.FromMinutes(10));
        // Let pass and then moved to another time: accepted at once, or still waiting, they stay let pass.
        var moved = await SalonAsync(fixture, "movida", "Estética Movida");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, moved, TimeSpan.FromMinutes(5));
        for (var n = 0; n < 4; n++)
        {
            var lapsed = await RequestAsync(fixture, moved, Now.AddHours(-5), startAt: Now.AddHours(-3));
            lapsed.Update("Valeria", "+525522107765", "Corte", Now.AddDays(1), Now.AddDays(1).AddHours(1), null, null);
            if (n % 2 == 0)
                lapsed.Accept(Now.AddHours(-1));
        }
        // Three quick answers each, and four that are already a day or more: three of seven is not half.
        // Still waiting after a day, for a time days ahead.
        var waiting = await SalonAsync(fixture, "espera", "Estética Espera");
        // Its customers cancelled after a day without an answer.
        var given = await SalonAsync(fixture, "cancelada", "Estética Cancelada");
        // Less than a day: still waiting, or cancelled sooner, they do not count yet, and it answers within an hour.
        var soon = await SalonAsync(fixture, "pronto", "Estética Pronto");
        foreach (var salon in new[] { waiting, given, soon })
        {
            for (var n = 0; n < 3; n++)
                await AnsweredAsync(fixture, salon, TimeSpan.FromMinutes(5));
        }
        for (var n = 0; n < 4; n++)
        {
            await RequestAsync(fixture, waiting, Now.AddHours(-24 - n));
            var cancelled = await RequestAsync(fixture, given, Now.AddDays(-3));
            cancelled.CancelByCustomer(Now.AddDays(-3).AddHours(24 + n));
        }
        for (var n = 0; n < 4; n++)
        {
            await RequestAsync(fixture, soon, Now.AddHours(-23));
            var early = await RequestAsync(fixture, soon, Now.AddDays(-3));
            early.CancelByCustomer(Now.AddDays(-3).AddHours(23));
        }
        await fixture.Context.SaveChangesAsync();

        var found = await CardsAsync(fixture);

        Assert.Null(found["tarde"].ResponseTime);
        // Counted by their new time, two would be quick and the other two would not count yet: within an hour.
        Assert.Null(found["movida"].ResponseTime);
        Assert.Null(found["espera"].ResponseTime);
        Assert.Null(found["cancelada"].ResponseTime);
        Assert.Equal(ResponseTime.WithinAnHour, found["pronto"].ResponseTime);
    }

    [Fact]
    public async Task Card_ShouldCountOnlyTheRequestsOnlineOfTheLast90Days_OfItsOwnBusiness_WhileItTakesThem()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "sol", "Estética Sol");
        var other = await SalonAsync(fixture, "luna", "Estética Luna");
        await AnsweredAsync(fixture, salon, TimeSpan.FromMinutes(20));
        await AnsweredAsync(fixture, salon, TimeSpan.FromMinutes(20));
        // Slow, but before the last 90 days: they would make it slower if they counted (two of four is still half, of five
        // it is not).
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, salon, TimeSpan.FromHours(30), arrivedDaysAgo: ResponseTimes.WindowDays + 1);
        // Booked by the team: no request.
        fixture.Context.Appointments.Add(new Appointment(salon.Id, "Ana", "5522107765", "Corte", Now.AddDays(1), Now.AddDays(1).AddHours(1))
        {
            CreatedAt = Now.AddDays(-1)
        });
        // The slow answers of another business are its own.
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, other, TimeSpan.FromHours(30));
        // It stopped booking online: its card says nothing of requests it no longer takes.
        var stopped = await SalonAsync(fixture, "sin-agenda", "Estética Sin Agenda", bookingOn: false, product: true);
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, stopped, TimeSpan.FromMinutes(5));
        await fixture.Context.SaveChangesAsync();

        var found = await CardsAsync(fixture);

        // Two count: fewer than three say nothing.
        Assert.Null(found["sol"].ResponseTime);
        Assert.Null(found["luna"].ResponseTime);
        Assert.Null(found["sin-agenda"].ResponseTime);

        // A third answer within the last days, slow: two of three within an hour.
        await AnsweredAsync(fixture, salon, TimeSpan.FromHours(30), arrivedDaysAgo: ResponseTimes.WindowDays - 1);
        Assert.Equal(ResponseTime.WithinAnHour, (await CardsAsync(fixture))["sol"].ResponseTime);
    }

    [Fact]
    public async Task WhatBecameSlowBeforeTheFirstAnswerKept_ShouldNotCount()
    {
        var deployedAt = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
        await using var fixture = Fixture(deployedAt.AddDays(5));
        var salon = await SalonAsync(fixture, "a-sol", "A Sol");
        await SalonAsync(fixture, "b-luna", "B Luna");
        // Let pass while the version before Fase 29J ran, which kept no answers (those it answered do not count either).
        for (var n = 0; n < 4; n++)
            await RequestAsync(fixture, salon, deployedAt.AddDays(-14), startAt: deployedAt.AddDays(-13));
        // Their day passed then too, though their time is still ahead, or their customers cancelled after it.
        var leftBehind = new List<Appointment>();
        for (var n = 0; n < 4; n++)
        {
            leftBehind.Add(await RequestAsync(fixture, salon, deployedAt.AddDays(-3), startAt: deployedAt.AddDays(20)));
            var cancelled = await RequestAsync(fixture, salon, deployedAt.AddDays(-5), startAt: deployedAt.AddDays(20));
            cancelled.CancelByCustomer(deployedAt.AddDays(-5).AddHours(30));
            // Arrived hours before the first answer kept, for a time that began just before it: its day had not passed.
            await RequestAsync(fixture, salon, deployedAt.AddHours(-6), startAt: deployedAt);
        }
        await fixture.Context.SaveChangesAsync();

        // Nothing kept yet, nothing counts: it is not slow, it goes by name with the other one without requests.
        Assert.Equal(["a-sol", "b-luna"], await CategoryAsync(fixture));

        // From the first answer kept on: three quick ones of seven would not be half, of three they are.
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, salon, TimeSpan.FromMinutes(15), arrivedDaysAgo: 0, clock: deployedAt.AddHours(n));
        Assert.Equal(ResponseTime.WithinAnHour, (await CardsAsync(fixture))["a-sol"].ResponseTime);
        // Answering what was left behind does not make it slower: those were already late before the first answer kept.
        foreach (var request in leftBehind)
            request.Decline(deployedAt.AddDays(1));
        await fixture.Context.SaveChangesAsync();
        Assert.Equal(ResponseTime.WithinAnHour, (await CardsAsync(fixture))["a-sol"].ResponseTime);

        // Those let pass after it count: three quick ones of seven are not half, and it goes after the one without requests.
        for (var n = 0; n < 4; n++)
            await RequestAsync(fixture, salon, deployedAt.AddDays(1), startAt: deployedAt.AddDays(2));
        Assert.Null((await CardsAsync(fixture))["a-sol"].ResponseTime);
        Assert.Equal(["b-luna", "a-sol"], await CategoryAsync(fixture));
    }

    [Fact]
    public async Task ATradeThatOnlyReceivesVisits_ShouldSayHowSoonItAnswersTheVisitsRequested()
    {
        await using var fixture = Fixture();
        var (plumber, zone) = await TradeAsync(fixture, "plomeria", "Plomería Hernández");
        // Three customers (Explorar counts one per customer, Fase 29M), each arrived on its own day.
        for (var n = 0; n < 3; n++)
        {
            var arrivedAt = Now.AddDays(-3 - n);
            var visit = Appointment.RequestHomeVisit(
                plumber.Id, "Valeria", NewCustomerPhone(), null, HomeVisitRules.DefaultVisitName, Now.AddDays(2), Now.AddDays(2).AddHours(1),
                "Gotea el calentador", null, DeliveryAddress.ForVisit("72000", "Av. Juárez 123", null), zone,
                HomeVisitChargePolicy.Charged, null, Guid.NewGuid(), (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..43]);
            visit.CreatedAt = arrivedAt;
            visit.Accept(arrivedAt.AddMinutes(20));
            fixture.Context.Appointments.Add(visit);
        }
        await fixture.Context.SaveChangesAsync();

        Assert.Equal(ResponseTime.WithinAnHour, (await CardsAsync(fixture))["plomeria"].ResponseTime);
    }

    [Fact]
    public async Task WhatAnEarlierVersionAccepted_ShouldNotCount_ThoughItsCustomerCancelsAfterADay()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "sol", "Estética Sol");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, salon, TimeSpan.FromMinutes(10));
        // Accepted within minutes by the version before Fase 29J, which kept neither the answer nor the time asked for, and
        // cancelled by its customer days later: it was not waiting (three quick ones of seven would not be half).
        for (var n = 0; n < 4; n++)
        {
            var accepted = await RequestAsync(fixture, salon, Now.AddDays(-3));
            accepted.Accept(Now.AddDays(-3).AddMinutes(10));
            fixture.Context.Entry(accepted).Property(nameof(Appointment.RespondedAt)).CurrentValue = null;
            fixture.Context.Entry(accepted).Property(nameof(Appointment.RequestedStartAt)).CurrentValue = null;
            accepted.CancelByCustomer(Now.AddDays(-1));
        }
        await fixture.Context.SaveChangesAsync();

        Assert.Equal(ResponseTime.WithinAnHour, (await CardsAsync(fixture))["sol"].ResponseTime);
    }

    [Fact]
    public async Task ACategory_ShouldPutFirstWhoAnswersSooner_WithinItsGroup_AndTheUnknownInTheMiddle()
    {
        await using var fixture = Fixture();
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
        var stopped = await SalonAsync(fixture, "f-sin-agenda", "F Sin Agenda", bookingOn: false, product: true);
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, stopped, TimeSpan.FromMinutes(5));

        var category = await Handler(fixture).Handle(new GetPublicDirectoryQuery(BusinessCategory.Beauty), CancellationToken.None);
        var everyone = await Handler(fixture).Handle(new GetPublicDirectoryQuery(), CancellationToken.None);

        Assert.Equal(
            ["c-rapida", "b-nueva", "e-doce", "d-un-dia", "a-lenta", "f-sin-agenda"],
            category.Businesses.Items.Select(b => b.Slug));
        Assert.Equal(
            [ResponseTime.WithinAnHour, null, ResponseTime.WithinTwelveHours, ResponseTime.WithinADay, null, null],
            category.Businesses.Items.Select(b => b.ResponseTime));
        // Since Fase 29S (ADR 0073) the whole list keeps the same order as the category.
        Assert.Equal(
            ["c-rapida", "b-nueva", "e-doce", "d-un-dia", "a-lenta", "f-sin-agenda"],
            everyone.Businesses.Items.Select(b => b.Slug));
    }

    [Fact]
    public async Task ARequestDeclinedAsFake_ShouldNotCount_NorTakeThePlaceOfItsCustomersLast_UntilTheMarkIsTakenBack()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "falsas", "Estética Falsas");
        // One quick answer and two let pass, of three customers.
        await AnsweredAsync(fixture, salon, TimeSpan.FromMinutes(30));
        await LapsedAsync(fixture, salon);
        await LapsedAsync(fixture, salon);
        // A customer answered quickly before, whose last request was let pass; and another one let pass.
        var known = NewCustomerPhone();
        var before = await RequestAsync(fixture, salon, Now.AddDays(-5), phone: known);
        before.Accept(Now.AddDays(-5).AddMinutes(20));
        var later = await RequestAsync(fixture, salon, Now.AddDays(-2), startAt: Now.AddHours(-1), phone: known);
        var stranger = await RequestAsync(fixture, salon, Now.AddDays(-2), startAt: Now.AddHours(-1));
        await fixture.Context.SaveChangesAsync();
        // One of five within an hour: nothing to say.
        Assert.Null((await CardsAsync(fixture))["falsas"].ResponseTime);

        // Declined as fake, they do not count, and the customer's quick answer before counts again: two of four.
        later.MarkFake(Now);
        stranger.MarkFake(Now);
        await fixture.Context.SaveChangesAsync();
        Assert.Equal(ResponseTime.WithinAnHour, (await CardsAsync(fixture))["falsas"].ResponseTime);

        // Taken back, they count again, declined now: past their limit, among the slowest.
        later.UnmarkFake();
        stranger.UnmarkFake();
        await fixture.Context.SaveChangesAsync();
        Assert.Null((await CardsAsync(fixture))["falsas"].ResponseTime);
    }

    [Fact]
    public async Task TheAgenda_ShouldKeepWhenItsTeamAnswered_ByTheStatusToo()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "sol", "Estética Sol");
        var accepted = await RequestAsync(fixture, salon, Now.AddHours(-2));
        var declined = await RequestAsync(fixture, salon, Now.AddHours(-1));
        fixture.TenantService.SetTenant(salon.Id);
        var status = new UpdateAppointmentStatusCommandHandler(
            fixture.Context, new AppointmentNotices(fixture.Context, new PhoneNormalizationService(), fixture.Clock), clock: fixture.Clock);

        Assert.True((await status.Handle(new UpdateAppointmentStatusCommand { AppointmentId = accepted.Id, NewStatus = AppointmentStatus.Scheduled }, CancellationToken.None)).IsSuccess);
        Assert.True((await status.Handle(new UpdateAppointmentStatusCommand { AppointmentId = declined.Id, NewStatus = AppointmentStatus.Cancelled }, CancellationToken.None)).IsSuccess);

        Assert.Equal((Now, Now), (accepted.RespondedAt, declined.RespondedAt));
    }

    internal static SalesTestFixture Fixture(DateTimeOffset? now = null)
    {
        var fixture = SalesTestFixture.Create();
        fixture.Clock.UtcNow = now ?? Now;
        return fixture;
    }

    /// <summary>A salon that books online every day, its menu published; with a product, its menu shows something without it.</summary>
    internal static async Task<Tenant> SalonAsync(SalesTestFixture fixture, string slug, string name, bool bookingOn = true, bool product = false)
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
        if (product)
            fixture.Context.Products.Add(new Product(tenant.Id, category.Id, "Shampoo", null, 90m, "MXN"));
        await fixture.Context.SaveChangesAsync();
        return tenant;
    }

    /// <summary>A plumber that only receives visits, in a zone of the 72000, without booking online.</summary>
    internal static async Task<(Tenant Tenant, DeliveryZone Zone)> TradeAsync(SalesTestFixture fixture, string slug, string name)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Service, TenantCapability.Service | TenantCapability.Appointments, slug: slug);
        tenant.Update(name, $"{slug}@demo.com", null, null);
        tenant.SetDirectoryProfile(BusinessCategory.HomeRepairs, null);
        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);
        settings.StartReceivingHomeVisits();
        settings.UpdateOnlineMenu(OnlineMenuMode.MenuOnly);
        settings.SetOpeningHours(OpeningHours.Create(EveryDay.Select(d => new OpeningInterval(d, TimeOnly.MinValue, new TimeOnly(23, 59)))));
        var zone = new DeliveryZone(tenant.Id, new DeliveryZoneFields("Centro", 50m, 0m, null, ["72000"]));
        fixture.Context.DeliveryZones.Add(zone);
        await fixture.Context.SaveChangesAsync();
        return (tenant, zone);
    }

    /// <summary>
    /// A request online that arrived <paramref name="arrivedAt"/>, for two days from now unless said, still waiting; from a
    /// customer of its own, unless <paramref name="phone"/> says whose (Explorar counts one per customer, Fase 29M).
    /// </summary>
    internal static async Task<Appointment> RequestAsync(
        SalesTestFixture fixture, Tenant tenant, DateTimeOffset arrivedAt, DateTimeOffset? startAt = null, string? phone = null,
        bool fromTeam = false)
    {
        var start = startAt ?? fixture.Clock.UtcNow.AddDays(2);
        var request = Appointment.RequestOnline(
            tenant.Id, "Valeria", phone ?? NewCustomerPhone(), Guid.NewGuid(), "Corte", start, start.AddHours(1), null, null, Guid.NewGuid(),
            (Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))[..43], fromTeam: fromTeam);
        request.CreatedAt = arrivedAt;
        fixture.Context.Appointments.Add(request);
        await fixture.Context.SaveChangesAsync();
        return request;
    }

    private static int _customers;

    /// <summary>The phone of a customer no other request has, in E.164.</summary>
    internal static string NewCustomerPhone() => $"+5255{Interlocked.Increment(ref _customers):D8}";

    /// <summary>
    /// A request the business accepted (or declined) after its customer waited <paramref name="waited"/>; for two days from
    /// now, or <paramref name="startsAfter"/> it arrived.
    /// </summary>
    internal static async Task AnsweredAsync(
        SalesTestFixture fixture, Tenant tenant, TimeSpan waited, int arrivedDaysAgo = 3, bool declined = false,
        TimeSpan? startsAfter = null, DateTimeOffset? clock = null)
    {
        var arrivedAt = (clock ?? Now).AddDays(-arrivedDaysAgo);
        var request = await RequestAsync(fixture, tenant, arrivedAt, startsAfter is { } after ? arrivedAt + after : null);
        if (declined)
            request.Decline(arrivedAt + waited);
        else
            request.Accept(arrivedAt + waited);
        await fixture.Context.SaveChangesAsync();
    }

    /// <summary>A request whose time began without an answer.</summary>
    internal static Task LapsedAsync(SalesTestFixture fixture, Tenant tenant)
        => RequestAsync(fixture, tenant, Now.AddDays(-2), startAt: Now.AddHours(-1));

    internal static GetPublicDirectoryQueryHandler Handler(SalesTestFixture fixture)
        => new(fixture.Context, fixture.Clock, new PublicDirectoryCache(), new PublicProductIndexCache(), NullLogger<GetPublicDirectoryQueryHandler>.Instance);

    private static async Task<List<string>> CategoryAsync(SalesTestFixture fixture)
        => (await Handler(fixture).Handle(new GetPublicDirectoryQuery(BusinessCategory.Beauty), CancellationToken.None))
            .Businesses.Items.Select(b => b.Slug).ToList();

    internal static async Task<Dictionary<string, PublicDirectoryBusinessDto>> CardsAsync(SalesTestFixture fixture)
        => (await Handler(fixture).Handle(new GetPublicDirectoryQuery(), CancellationToken.None))
            .Businesses.Items.ToDictionary(b => b.Slug);
}
