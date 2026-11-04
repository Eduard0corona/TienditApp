using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static FoodParkDigital.Application.Tests.ExploreResponseTimeTests;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// How soon a business answers, outside Explorar (Fase 29K, ADR 0065): its store, its booking page and its visit request
/// say what its card in Explorar says, from the same snapshot, and its owner reads it in «Cómo te ven en TienditApp», the
/// slower ones too.
/// </summary>
public class ResponseTimeOutsideExploreTests
{
    [Fact]
    public async Task TheStore_TheBookingPage_AndTheVisitRequest_ShouldSayWhatItsCardSays()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "rapida", "Estética Rápida");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, salon, TimeSpan.FromMinutes(20));
        var (plumber, _) = await TradeAsync(fixture, "plomeria", "Plomería Hernández");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, plumber, TimeSpan.FromHours(2));
        var cards = await CardsAsync(fixture);

        Assert.Equal(ResponseTime.WithinAnHour, cards["rapida"].ResponseTime);
        Assert.Equal(ResponseTime.WithinAnHour, (await StoreAsync(fixture, "rapida"))!.ResponseTime);
        Assert.Equal(ResponseTime.WithinAnHour, (await BookingAsync(fixture, "rapida"))!.ResponseTime);

        Assert.Equal(ResponseTime.WithinThreeHours, cards["plomeria"].ResponseTime);
        Assert.Equal(ResponseTime.WithinThreeHours, (await StoreAsync(fixture, "plomeria"))!.ResponseTime);
        Assert.Equal(ResponseTime.WithinThreeHours, (await VisitAsync(fixture, "plomeria"))!.ResponseTime);
    }

    [Fact]
    public async Task APage_ShouldSayNothing_WhenItsCardSaysNothing_OrItNoLongerTakesRequests()
    {
        await using var fixture = Fixture();
        // A day or more: Explorar never publishes it.
        var slow = await SalonAsync(fixture, "lenta", "Estética Lenta");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, slow, TimeSpan.FromHours(24));
        // Two requests are not enough.
        var newer = await SalonAsync(fixture, "nueva", "Estética Nueva");
        for (var n = 0; n < 2; n++)
            await AnsweredAsync(fixture, newer, TimeSpan.FromMinutes(10));
        // Its owner keeps it out of Explorar: its store says nothing either.
        var hidden = await SalonAsync(fixture, "oculta", "Estética Oculta", product: true);
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, hidden, TimeSpan.FromMinutes(10));
        (await SettingsAsync(fixture, hidden)).SetDirectoryListing(false);
        await fixture.Context.SaveChangesAsync();

        Assert.Null((await StoreAsync(fixture, "lenta"))!.ResponseTime);
        Assert.Null((await BookingAsync(fixture, "lenta"))!.ResponseTime);
        Assert.Null((await StoreAsync(fixture, "nueva"))!.ResponseTime);
        Assert.Null((await StoreAsync(fixture, "oculta"))!.ResponseTime);

        // It stops booking online after the snapshot said it answers within an hour: its store reads it as it is now.
        var stopped = await SalonAsync(fixture, "cerro", "Estética Cerró", product: true);
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, stopped, TimeSpan.FromMinutes(10));
        var cache = new PublicDirectoryCache();
        Assert.Equal(ResponseTime.WithinAnHour, (await StoreAsync(fixture, "cerro", cache))!.ResponseTime);
        (await SettingsAsync(fixture, stopped)).UpdateOnlineBooking(false, 1);
        await fixture.Context.SaveChangesAsync();

        var menu = await StoreAsync(fixture, "cerro", cache);
        Assert.False(menu!.BooksOnline);
        Assert.Null(menu.ResponseTime);
    }

    [Fact]
    public async Task ThePages_ShouldTakeASnapshotUpTo10MinutesOld_AndRebuildAnOlderOne()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "rapida", "Estética Rápida");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, salon, TimeSpan.FromMinutes(20));
        var cache = new PublicDirectoryCache();
        Assert.Equal(ResponseTime.WithinAnHour, (await StoreAsync(fixture, "rapida", cache))!.ResponseTime);

        // Four slow ones later: within an hour is no longer half.
        for (var n = 0; n < 4; n++)
            await AnsweredAsync(fixture, salon, TimeSpan.FromHours(30));

        fixture.Clock.UtcNow = Now + PublicResponseTime.MaxAge - TimeSpan.FromSeconds(1);
        Assert.Equal(ResponseTime.WithinAnHour, (await StoreAsync(fixture, "rapida", cache))!.ResponseTime);
        Assert.Equal(ResponseTime.WithinAnHour, (await BookingAsync(fixture, "rapida", cache))!.ResponseTime);

        fixture.Clock.UtcNow = Now + PublicResponseTime.MaxAge;
        Assert.Null((await StoreAsync(fixture, "rapida", cache))!.ResponseTime);
    }

    [Fact]
    public async Task ASnapshotBuiltFromAStore_ShouldListEveryBusiness_AsExplorarDoes()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "rapida", "Estética Rápida");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, salon, TimeSpan.FromMinutes(20));
        var other = await SalonAsync(fixture, "horas", "Estética Horas");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, other, TimeSpan.FromHours(2));

        // The store enters its business before it reads the snapshot; the snapshot it builds is Explorar's, for everyone.
        var cache = new PublicDirectoryCache();
        Assert.Equal(ResponseTime.WithinAnHour, (await StoreAsync(fixture, "rapida", cache))!.ResponseTime);
        var found = (await new GetPublicDirectoryQueryHandler(
                    fixture.Context, fixture.Clock, cache, new PublicProductIndexCache(), NullLogger<GetPublicDirectoryQueryHandler>.Instance)
                .Handle(new GetPublicDirectoryQuery(), CancellationToken.None))
            .Businesses.Items.ToDictionary(b => b.Slug);

        Assert.Equal(ResponseTime.WithinAnHour, found["rapida"].ResponseTime);
        Assert.Equal(ResponseTime.WithinThreeHours, found["horas"].ResponseTime);
    }

    [Fact]
    public async Task ASnapshotThatCannotBeRead_ShouldLeaveThePageWithoutIt()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "rapida", "Estética Rápida");
        var broken = SalesTestFixture.Create();
        await broken.DisposeAsync();

        var entry = await PublicResponseTime.EntryAsync(
            new PublicDirectoryCache(), broken.Context, Now, salon.Id, NullLogger.Instance, CancellationToken.None);

        Assert.Null(entry);
    }

    [Fact]
    public async Task TheOwner_ShouldReadHowSoonItAnswers_TheSlowerToo_WhileExplorarCountsItsRequests()
    {
        await using var fixture = Fixture();
        var quick = await SalonAsync(fixture, "rapida", "Estética Rápida");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, quick, TimeSpan.FromMinutes(20));
        var slow = await SalonAsync(fixture, "lenta", "Estética Lenta");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, slow, TimeSpan.FromHours(30));
        var newer = await SalonAsync(fixture, "nueva", "Estética Nueva");
        await AnsweredAsync(fixture, newer, TimeSpan.FromMinutes(10));
        // A shop that books nothing online: Explorar lists it, but counts no requests.
        var shop = await SalonAsync(fixture, "tienda", "Estética Tienda", bookingOn: false, product: true);
        // Kept out of Explorar: nothing is counted for its card.
        var hidden = await SalonAsync(fixture, "oculta", "Estética Oculta", product: true);
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, hidden, TimeSpan.FromMinutes(10));
        (await SettingsAsync(fixture, hidden)).SetDirectoryListing(false);
        await fixture.Context.SaveChangesAsync();

        Assert.Equal((true, (ResponseTime?)ResponseTime.WithinAnHour), await ProfileAsync(fixture, quick));
        Assert.Equal((true, (ResponseTime?)ResponseTime.Slower), await ProfileAsync(fixture, slow));
        Assert.Equal((true, (ResponseTime?)null), await ProfileAsync(fixture, newer));
        Assert.Equal((false, (ResponseTime?)null), await ProfileAsync(fixture, shop));
        Assert.Equal((false, (ResponseTime?)null), await ProfileAsync(fixture, hidden));

        // Hidden after the snapshot counted it: its owner reads it as it is now.
        var cache = new PublicDirectoryCache();
        Assert.Equal((true, (ResponseTime?)ResponseTime.WithinAnHour), await ProfileAsync(fixture, quick, cache));
        (await SettingsAsync(fixture, quick)).SetDirectoryListing(false);
        await fixture.Context.SaveChangesAsync();
        Assert.Equal((false, (ResponseTime?)null), await ProfileAsync(fixture, quick, cache));

        // Saving its card answers with it too.
        fixture.TenantService.SetTenant(slow.Id);
        var saved = await new UpdateDirectoryProfileCommandHandler(
                fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock, new PublicDirectoryCache(),
                NullLogger<UpdateDirectoryProfileCommandHandler>.Instance)
            .Handle(new UpdateDirectoryProfileCommand(BusinessCategory.Beauty, "Cortes y peinados", true), CancellationToken.None);
        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Equal((true, (ResponseTime?)ResponseTime.Slower), (saved.Value!.CountsRequests, saved.Value.ResponseTime));
    }

    private static Task<TenantSettings> SettingsAsync(SalesTestFixture fixture, Tenant tenant)
        => fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);

    private static Task<PublicMenuDto?> StoreAsync(SalesTestFixture fixture, string slug, PublicDirectoryCache? cache = null)
        => new GetPublicMenuQueryHandler(
                fixture.Context, fixture.TenantService, fixture.Clock, FakeOnlinePaymentGateway.Off, cache ?? new PublicDirectoryCache(),
                NullLogger<GetPublicMenuQueryHandler>.Instance)
            .Handle(new GetPublicMenuQuery(slug), CancellationToken.None);

    private static Task<PublicBookingDto?> BookingAsync(SalesTestFixture fixture, string slug, PublicDirectoryCache? cache = null)
        => new GetPublicBookingQueryHandler(
                fixture.Context, fixture.TenantService, fixture.Clock, cache ?? new PublicDirectoryCache(),
                NullLogger<GetPublicBookingQueryHandler>.Instance)
            .Handle(new GetPublicBookingQuery(slug), CancellationToken.None);

    private static Task<PublicHomeVisitDto?> VisitAsync(SalesTestFixture fixture, string slug)
        => new GetPublicHomeVisitQueryHandler(
                fixture.Context, fixture.TenantService, fixture.Clock, new PublicDirectoryCache(),
                NullLogger<GetPublicHomeVisitQueryHandler>.Instance)
            .Handle(new GetPublicHomeVisitQuery(slug), CancellationToken.None);

    private static async Task<(bool CountsRequests, ResponseTime? ResponseTime)> ProfileAsync(
        SalesTestFixture fixture, Tenant tenant, PublicDirectoryCache? cache = null)
    {
        fixture.TenantService.SetTenant(tenant.Id);
        var profile = await new GetDirectoryProfileQueryHandler(
                fixture.Context, fixture.TenantService, fixture.Clock, cache ?? new PublicDirectoryCache(),
                NullLogger<GetDirectoryProfileQueryHandler>.Instance)
            .Handle(new GetDirectoryProfileQuery(), CancellationToken.None);
        Assert.True(profile.IsSuccess, profile.Error);
        return (profile.Value!.CountsRequests, profile.Value.ResponseTime);
    }
}
