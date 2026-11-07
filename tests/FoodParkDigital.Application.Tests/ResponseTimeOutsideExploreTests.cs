using System.Linq.Expressions;
using FoodParkDigital.Application.OnlineBooking;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

        // Hidden from Explorar after the snapshot listed it: its pages read it as it is now too.
        var quick = await SalonAsync(fixture, "rapida", "Estética Rápida", product: true);
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, quick, TimeSpan.FromMinutes(10));
        cache = new PublicDirectoryCache();
        Assert.Equal(ResponseTime.WithinAnHour, (await StoreAsync(fixture, "rapida", cache))!.ResponseTime);
        (await SettingsAsync(fixture, quick)).SetDirectoryListing(false);
        await fixture.Context.SaveChangesAsync();
        Assert.Null((await StoreAsync(fixture, "rapida", cache))!.ResponseTime);
        Assert.Null((await BookingAsync(fixture, "rapida", cache))!.ResponseTime);

        var (plumber, _) = await TradeAsync(fixture, "plomeria", "Plomería Hernández");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, plumber, TimeSpan.FromMinutes(10));
        cache = new PublicDirectoryCache();
        Assert.Equal(ResponseTime.WithinAnHour, (await VisitAsync(fixture, "plomeria", cache))!.ResponseTime);
        (await SettingsAsync(fixture, plumber)).SetDirectoryListing(false);
        await fixture.Context.SaveChangesAsync();
        Assert.Null((await VisitAsync(fixture, "plomeria", cache))!.ResponseTime);
    }

    [Fact]
    public async Task AStoreAtATable_ShouldNotSayIt_OnlyTheOrderIsRequestedThere()
    {
        await using var fixture = Fixture();
        var salon = await SalonAsync(fixture, "rapida", "Estética Rápida", product: true);
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, salon, TimeSpan.FromMinutes(20));

        Assert.Equal(ResponseTime.WithinAnHour, (await StoreAsync(fixture, "rapida"))!.ResponseTime);
        Assert.Null((await StoreAsync(fixture, "rapida", table: "Mesa 4"))!.ResponseTime);
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

        // Four answered in two hours later: within an hour is no longer half, within three hours is.
        for (var n = 0; n < 4; n++)
            await AnsweredAsync(fixture, salon, TimeSpan.FromHours(2));

        fixture.Clock.UtcNow = Now + PublicResponseTime.MaxAge - TimeSpan.FromSeconds(1);
        Assert.Equal(ResponseTime.WithinAnHour, (await StoreAsync(fixture, "rapida", cache))!.ResponseTime);
        Assert.Equal(ResponseTime.WithinAnHour, (await BookingAsync(fixture, "rapida", cache))!.ResponseTime);

        // Only a snapshot rebuilt says it.
        fixture.Clock.UtcNow = Now + PublicResponseTime.MaxAge;
        Assert.Equal(ResponseTime.WithinThreeHours, (await StoreAsync(fixture, "rapida", cache))!.ResponseTime);
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
    public void ThePages_ShouldTakeA10MinuteSnapshot_AndLeaveAFailedRebuildAloneAMinute()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), PublicResponseTime.MaxAge);
        Assert.Equal(TimeSpan.FromMinutes(1), PublicDirectoryCache.FailureBackoff);
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
        // Its own time limit too (a cancellation the page did not ask for).
        Assert.Null(await EntryAsync(new PublicDirectoryCache(), Now, (_, _) => throw new OperationCanceledException()));
    }

    [Fact]
    public async Task ACustomerWhoLeaves_ShouldCancelThePage()
    {
        using var left = new CancellationTokenSource();
        await left.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PublicResponseTime.EntryAsync(
            new PublicDirectoryCache(), Now, Guid.NewGuid(), (now, _) => Task.FromResult(Empty(now)), PublicResponseTime.MaxAge,
            NullLogger.Instance, left.Token));
    }

    [Fact]
    public async Task AFailedRebuild_ShouldBeLeftAloneAMinute_ByThePagesThatFollow()
    {
        var cache = new PublicDirectoryCache();
        var builds = 0;
        Task<PublicDirectorySnapshot> Failing(DateTimeOffset now, CancellationToken ct)
        {
            builds++;
            throw new InvalidOperationException("La base no responde.");
        }

        Assert.Null(await EntryAsync(cache, Now, Failing));
        Assert.Null(await EntryAsync(cache, Now + PublicDirectoryCache.FailureBackoff - TimeSpan.FromSeconds(1), Failing));
        Assert.Equal(1, builds);

        Assert.Null(await EntryAsync(cache, Now + PublicDirectoryCache.FailureBackoff, Failing));
        Assert.Equal(2, builds);
        // Explorar itself still rebuilds it, and one that works ends the wait.
        await cache.GetAsync(Now + PublicDirectoryCache.FailureBackoff, (now, _) => Task.FromResult(Empty(now)), CancellationToken.None);
        builds = 0;
        await EntryAsync(cache, Now + PublicDirectoryCache.FailureBackoff + PublicResponseTime.MaxAge, (now, _) =>
        {
            builds++;
            return Task.FromResult(Empty(now));
        });
        Assert.Equal(1, builds);
    }

    [Fact]
    public async Task ARebuildThatWorks_ShouldEndTheWaitOfTheOneThatFailed()
    {
        var cache = new PublicDirectoryCache();
        var oneSecond = TimeSpan.FromSeconds(1);
        var builds = 0;
        Task<PublicDirectorySnapshot> Working(DateTimeOffset now, CancellationToken ct)
        {
            builds++;
            return Task.FromResult(Empty(now));
        }

        await PublicResponseTime.EntryAsync(
            cache, Now, Guid.NewGuid(), (_, _) => throw new InvalidOperationException("La base no responde."), oneSecond,
            NullLogger.Instance, CancellationToken.None);
        await cache.GetAsync(Now.AddSeconds(1), Working, CancellationToken.None);
        // Older than the second this page takes, well within the minute after the failure: it rebuilds it.
        await PublicResponseTime.EntryAsync(cache, Now.AddSeconds(3), Guid.NewGuid(), Working, oneSecond, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(2, builds);
    }

    [Fact]
    public async Task ASnapshotThatCouldNotReadHowSoonTheyAnswer_ShouldSayItForNobody()
    {
        await using var fixture = SalesTestFixture.Create(null, new FailingResponseTimes());
        fixture.Clock.UtcNow = Now;
        var salon = await SalonAsync(fixture, "rapida", "Estética Rápida");
        for (var n = 0; n < 3; n++)
            await AnsweredAsync(fixture, salon, TimeSpan.FromMinutes(20));

        var snapshot = await PublicDirectorySnapshot.BuildAsync(fixture.Context, Now, CancellationToken.None);

        Assert.False(Assert.Single(snapshot.Entries).ResponseTimeRead);
        Assert.Null(Assert.Single(snapshot.Entries).Business.ResponseTime);
        Assert.Equal((false, (ResponseTime?)null), await ProfileAsync(fixture, salon));
        Assert.Null((await StoreAsync(fixture, "rapida"))!.ResponseTime);
    }

    [Fact]
    public async Task APage_ShouldNotWaitInLine_WhileAnotherRebuildsTheSnapshot()
    {
        var cache = new PublicDirectoryCache();
        var release = new TaskCompletionSource<PublicDirectorySnapshot>();
        var first = EntryAsync(cache, Now, (_, _) => release.Task);
        var builds = 0;

        // It answers at once: waiting for the first would never end, since the first waits for this test.
        var second = await EntryAsync(cache, Now, (now, _) =>
        {
            builds++;
            return Task.FromResult(Empty(now));
        }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(second);
        Assert.Equal(0, builds);
        release.SetResult(Empty(Now));
        await first;
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

        // A snapshot that could not read how soon its businesses answer: its owner reads nothing, not «not enough».
        var read = await PublicDirectorySnapshot.BuildAsync(fixture.Context, Now, CancellationToken.None);
        var unread = new PublicDirectoryCache();
        await unread.GetAsync(
            Now, (now, _) => Task.FromResult(new PublicDirectorySnapshot(now, read.Entries.Select(entry => entry with { ResponseTimeRead = false }).ToList())),
            CancellationToken.None);
        Assert.Equal((false, (ResponseTime?)null), await ProfileAsync(fixture, slow, unread));

        // Saving its card answers with it too.
        fixture.TenantService.SetTenant(slow.Id);
        var saved = await new UpdateDirectoryProfileCommandHandler(
                fixture.Context, fixture.TenantService, fixture.CurrentUser, fixture.Clock, new PublicDirectoryCache(),
                NullLogger<UpdateDirectoryProfileCommandHandler>.Instance)
            .Handle(new UpdateDirectoryProfileCommand(BusinessCategory.Beauty, "Cortes y peinados", true), CancellationToken.None);
        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Equal((true, (ResponseTime?)ResponseTime.Slower), (saved.Value!.CountsRequests, saved.Value.ResponseTime));
    }

    private static PublicDirectorySnapshot Empty(DateTimeOffset builtAt) => new(builtAt, []);

    /// <summary>Fails the read of how soon the businesses answer (the first answer kept, its MIN), and nothing else.</summary>
    private sealed class FailingResponseTimes : IQueryExpressionInterceptor
    {
        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
            => new FirstAnswerKept().Finds(queryExpression)
                ? throw new InvalidOperationException("La base no responde.")
                : queryExpression;

        private sealed class FirstAnswerKept : ExpressionVisitor
        {
            private bool _found;

            public bool Finds(Expression expression)
            {
                Visit(expression);
                return _found;
            }

            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                if (node.Method.Name == nameof(Queryable.Min)
                    && node.Arguments[0].Type.GetGenericArguments().FirstOrDefault() == typeof(Appointment))
                    _found = true;
                return base.VisitMethodCall(node);
            }
        }
    }

    private static Task<PublicDirectorySnapshot.Entry?> EntryAsync(
        PublicDirectoryCache cache, DateTimeOffset utcNow, Func<DateTimeOffset, CancellationToken, Task<PublicDirectorySnapshot>> build)
        => PublicResponseTime.EntryAsync(cache, utcNow, Guid.NewGuid(), build, PublicResponseTime.MaxAge, NullLogger.Instance, CancellationToken.None);

    private static Task<TenantSettings> SettingsAsync(SalesTestFixture fixture, Tenant tenant)
        => fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);

    private static Task<PublicMenuDto?> StoreAsync(
        SalesTestFixture fixture, string slug, PublicDirectoryCache? cache = null, string? table = null)
        => new GetPublicMenuQueryHandler(
                fixture.Context, fixture.TenantService, fixture.Clock, FakeOnlinePaymentGateway.Off, cache ?? new PublicDirectoryCache(),
                NullLogger<GetPublicMenuQueryHandler>.Instance)
            .Handle(new GetPublicMenuQuery(slug, table), CancellationToken.None);

    private static Task<PublicBookingDto?> BookingAsync(SalesTestFixture fixture, string slug, PublicDirectoryCache? cache = null)
        => new GetPublicBookingQueryHandler(
                fixture.Context, fixture.TenantService, fixture.Clock, cache ?? new PublicDirectoryCache(),
                NullLogger<GetPublicBookingQueryHandler>.Instance)
            .Handle(new GetPublicBookingQuery(slug), CancellationToken.None);

    private static Task<PublicHomeVisitDto?> VisitAsync(SalesTestFixture fixture, string slug, PublicDirectoryCache? cache = null)
        => new GetPublicHomeVisitQueryHandler(
                fixture.Context, fixture.TenantService, fixture.Clock, cache ?? new PublicDirectoryCache(),
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
