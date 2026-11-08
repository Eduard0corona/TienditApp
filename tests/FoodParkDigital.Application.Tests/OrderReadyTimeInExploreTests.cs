using System.Linq.Expressions;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using static FoodParkDigital.Application.Tests.ExploreResponseTimeTests;
using static FoodParkDigital.Application.Tests.OrderConfirmTimeInExploreTests;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// How soon a kitchen has ready the orders its customers place on its online menu (Fase 29Y, ADR 0079): from when each
/// arrived until it was marked «Listo», in its card in Explorar, its menu and for its owner.
/// </summary>
public class OrderReadyTimeInExploreTests
{
    private static readonly IOrderStateMachine Kitchen = OrderStateMachineFactory.Create(BusinessType.Food, OrderType.Retail);

    [Fact]
    public async Task ACard_ShouldSayWithinWhatHalfItsOrdersWereReady_FromWhenTheyArrived_FromThreeOrders()
    {
        await using var fixture = Fixture();
        // Ready 10, 20 and 50 minutes after arriving, confirming included: two of three within half an hour.
        var tacos = await KitchenAsync(fixture, "tacos", "Tacos El Güero");
        foreach (var minutes in new[] { 10, 20, 50 })
            await ReadyOrderAsync(fixture, tacos, readyAfter: TimeSpan.FromMinutes(minutes));
        // Two ready at once: fewer than three say nothing.
        var pizza = await KitchenAsync(fixture, "pizza", "Pizzería Napoli");
        for (var n = 0; n < 2; n++)
            await ReadyOrderAsync(fixture, pizza, readyAfter: TimeSpan.FromMinutes(5));
        // Its drawer closed: its orders are paused, and its card still says it.
        var sushi = await KitchenAsync(fixture, "sushi", "Sushi Koi", drawerOpen: false);
        for (var n = 0; n < 3; n++)
            await ReadyOrderAsync(fixture, sushi, readyAfter: TimeSpan.FromMinutes(40));

        var cards = await CardsAsync(fixture);

        Assert.Equal(ReadyTime.WithinHalfAnHour, cards["tacos"].ReadyTime);
        Assert.Null(cards["pizza"].ReadyTime);
        Assert.Equal((false, true, ReadyTime.WithinAnHour), (cards["sushi"].AcceptsOrders, cards["sushi"].OrdersPaused, cards["sushi"].ReadyTime));
    }

    [Fact]
    public async Task OnlyOrdersMarkedReady_OfItsOnlineMenu_ShouldCount_OncePerCustomer_InTheLast90Days()
    {
        await using var fixture = Fixture();
        var tacos = await KitchenAsync(fixture, "tacos", "Tacos El Güero");
        // Three customers whose last order was ready within fifteen minutes, after two slow ones each: counting every order
        // would make it slow.
        foreach (var phone in new[] { "5530000001", "5530000002", "5530000003" })
        {
            await ReadyOrderAsync(fixture, tacos, readyAfter: TimeSpan.FromMinutes(70), arrivedDaysAgo: 6, phone: phone);
            await ReadyOrderAsync(fixture, tacos, readyAfter: TimeSpan.FromMinutes(70), arrivedDaysAgo: 5, phone: phone);
            await ReadyOrderAsync(fixture, tacos, readyAfter: TimeSpan.FromMinutes(10), arrivedDaysAgo: 2, phone: phone);
            // A later one handed over without being marked ready, or still in the kitchen: it does not take their place.
            await ReadyOrderAsync(fixture, tacos, readyAfter: null, handedOver: phone != "5530000003", arrivedDaysAgo: 1, phone: phone);
        }
        // A kitchen whose only ready orders do not count, three of each kind, so any one that counted would say something:
        // of a table, taken by the team, of the counter, or of more than 90 days ago.
        var pizza = await KitchenAsync(fixture, "pizza", "Pizzería Napoli");
        for (var n = 0; n < 3; n++)
        {
            await ReadyOrderAsync(fixture, pizza, readyAfter: TimeSpan.FromMinutes(90), table: "Mesa 4");
            await ReadyOrderAsync(fixture, pizza, readyAfter: TimeSpan.FromMinutes(90), byTheTeam: true);
            await ReadyOrderAsync(fixture, pizza, readyAfter: TimeSpan.FromMinutes(90), channel: OrderChannel.Counter, byTheTeam: true);
            await ReadyOrderAsync(fixture, pizza, readyAfter: TimeSpan.FromMinutes(90), arrivedDaysAgo: 91);
        }

        var snapshot = await SnapshotAsync(fixture);

        Assert.Equal(ReadyTime.WithinFifteenMinutes, Entry(snapshot, tacos).ReadyTime);
        Assert.Null(Entry(snapshot, pizza).ReadyTime);
    }

    [Fact]
    public async Task TheLimits_ShouldLeaveOut_ExactlyAQuarterOfAnHour_AndHalfAnHour_AndAnHourOrMore_ShouldBeAmongTheSlowest()
    {
        await using var fixture = Fixture();
        var exact = await KitchenAsync(fixture, "tacos", "Tacos El Güero");
        for (var n = 0; n < 3; n++)
            await ReadyOrderAsync(fixture, exact, readyAfter: TimeSpan.FromMinutes(15));
        var half = await KitchenAsync(fixture, "sushi", "Sushi Koi");
        for (var n = 0; n < 3; n++)
            await ReadyOrderAsync(fixture, half, readyAfter: TimeSpan.FromMinutes(30));
        var slow = await KitchenAsync(fixture, "pizza", "Pizzería Napoli");
        for (var n = 0; n < 3; n++)
            await ReadyOrderAsync(fixture, slow, readyAfter: TimeSpan.FromMinutes(60));

        var snapshot = await SnapshotAsync(fixture);

        Assert.Equal(ReadyTime.WithinHalfAnHour, Entry(snapshot, exact).ReadyTime);
        Assert.Equal(ReadyTime.WithinAnHour, Entry(snapshot, half).ReadyTime);
        Assert.Equal(ReadyTime.Slower, Entry(snapshot, slow).ReadyTime);
        Assert.Null((await CardsAsync(fixture))["pizza"].ReadyTime);
    }

    [Fact]
    public async Task TheOwner_ShouldReadHowSoonItsKitchenHasThemReady_TheSlowerToo_ButAStoreNever_AndItsMenuWhatItsCardSays()
    {
        await using var fixture = Fixture();
        var slow = await KitchenAsync(fixture, "tacos", "Tacos El Güero");
        for (var n = 0; n < 3; n++)
            await ReadyOrderAsync(fixture, slow, readyAfter: TimeSpan.FromMinutes(75));
        var quick = await KitchenAsync(fixture, "pizza", "Pizzería Napoli");
        for (var n = 0; n < 3; n++)
            await ReadyOrderAsync(fixture, quick, readyAfter: TimeSpan.FromMinutes(12));
        var newer = await KitchenAsync(fixture, "sushi", "Sushi Koi");
        // A menu that takes no orders (any more): neither its card nor its owner says anything of them.
        var menuOnly = await KitchenAsync(fixture, "cafe", "Café Luna", ordersOn: false);
        for (var n = 0; n < 3; n++)
            await ReadyOrderAsync(fixture, menuOnly, readyAfter: TimeSpan.FromMinutes(12));
        // A store marks no order ready: its owner reads how soon it confirms, never how soon it has them ready.
        var store = await StoreAsync(fixture, "papeleria", "Papelería Lápiz");

        var slowProfile = await ProfileAsync(fixture, slow);
        Assert.Equal((true, ReadyTime.Slower), (slowProfile.CountsReady, slowProfile.ReadyTime));
        Assert.Equal(ReadyTime.WithinFifteenMinutes, (await ProfileAsync(fixture, quick)).ReadyTime);
        var newerProfile = await ProfileAsync(fixture, newer);
        Assert.Equal((true, (ReadyTime?)null), (newerProfile.CountsReady, newerProfile.ReadyTime));
        Assert.False((await ProfileAsync(fixture, menuOnly)).CountsReady);
        var storeProfile = await ProfileAsync(fixture, store);
        Assert.Equal((true, false), (storeProfile.CountsOrders, storeProfile.CountsReady));

        var cards = await CardsAsync(fixture);
        Assert.Null(cards["tacos"].ReadyTime);
        Assert.Equal(ReadyTime.WithinFifteenMinutes, cards["pizza"].ReadyTime);
        Assert.Null(cards["cafe"].ReadyTime);
        // Its menu says what its card says, but not at a table.
        Assert.Null((await MenuAsync(fixture, "tacos"))!.ReadyTime);
        Assert.Equal(ReadyTime.WithinFifteenMinutes, (await MenuAsync(fixture, "pizza"))!.ReadyTime);
        Assert.Null((await MenuAsync(fixture, "pizza", table: "4"))!.ReadyTime);
    }

    [Fact]
    public async Task ASnapshotThatCouldNotReadTheReadyOrders_ShouldNotTellTheOwnerTheyAreNotCountedYet_ButStillHowSoonItConfirms()
    {
        await using var fixture = SalesTestFixture.Create(null, new FailingReadyRead());
        fixture.Clock.UtcNow = Now;
        var tacos = await KitchenAsync(fixture, "tacos", "Tacos El Güero");
        for (var n = 0; n < 3; n++)
            await ReadyOrderAsync(fixture, tacos, readyAfter: TimeSpan.FromMinutes(10));

        var profile = await ProfileAsync(fixture, tacos);
        var entry = Entry(await SnapshotAsync(fixture), tacos);

        Assert.Equal((false, (ReadyTime?)null), (profile.CountsReady, profile.ReadyTime));
        Assert.False(entry.ReadyTimeRead);
        // Each time from its own read: confirming them (a minute after arriving) still counts.
        Assert.Equal((true, OrderConfirmTime.WithinFiveMinutes), (profile.CountsOrders, profile.OrderConfirmTime));
        Assert.True(entry.OrderConfirmTimeRead);
    }

    [Fact]
    public async Task TheOwner_ShouldReadHowSoonItsKitchenHasThemReady_EvenWhenTheSnapshotCouldNotReadHowSoonItConfirms()
    {
        await using var fixture = SalesTestFixture.Create(null, new FailingConfirmRead());
        fixture.Clock.UtcNow = Now;
        var tacos = await KitchenAsync(fixture, "tacos", "Tacos El Güero");
        for (var n = 0; n < 3; n++)
            await ReadyOrderAsync(fixture, tacos, readyAfter: TimeSpan.FromMinutes(10));

        var profile = await ProfileAsync(fixture, tacos);

        Assert.Equal((false, true, ReadyTime.WithinFifteenMinutes), (profile.CountsOrders, profile.CountsReady, profile.ReadyTime));
        Assert.Equal(ReadyTime.WithinFifteenMinutes, (await CardsAsync(fixture))["tacos"].ReadyTime);
    }

    private static int _folio;

    /// <summary>
    /// An order its customer placed on the online menu <paramref name="arrivedDaysAgo"/>, confirmed a minute later, and marked
    /// «Listo» <paramref name="readyAfter"/> after it arrived; without it, handed over without being marked ready, or still in
    /// the kitchen.
    /// </summary>
    private static async Task ReadyOrderAsync(
        SalesTestFixture fixture, Tenant tenant, TimeSpan? readyAfter, bool handedOver = false, int arrivedDaysAgo = 3,
        string? phone = null, string? table = null, OrderChannel channel = OrderChannel.Online, bool byTheTeam = false)
    {
        var arrived = Now.AddDays(-arrivedDaysAgo);
        var order = new Order(
            tenant.Id, null, byTheTeam ? null : Guid.NewGuid(), byTheTeam, $"L-{Interlocked.Increment(ref _folio)}", BusinessType.Food,
            OrderType.Retail, "Valeria", phone ?? NewCustomerPhone(), null, table, channel: channel)
        {
            CreatedAt = arrived
        };
        order.TransitionTo(OrderStatus.Confirmed, Kitchen, "cajero@demo.com");
        if (readyAfter is not null)
            order.TransitionTo(OrderStatus.Ready, Kitchen, "cocina@demo.com");
        if (readyAfter is not null || handedOver)
            order.TransitionTo(OrderStatus.Delivered, Kitchen, "cajero@demo.com");

        fixture.Context.Orders.Add(order);
        // The history keeps when each change happened: created as it arrived, confirmed a minute later, ready after
        // readyAfter, and handed over five minutes after that.
        var ready = arrived + (readyAfter ?? TimeSpan.FromMinutes(20));
        foreach (var row in order.StatusHistory)
            fixture.Context.Entry(row).Property(h => h.ChangedAt).CurrentValue = row.NewStatus switch
            {
                _ when row.NewStatus == row.PreviousStatus => arrived,
                OrderStatus.Confirmed => arrived.AddMinutes(1),
                OrderStatus.Ready => ready,
                _ => ready.AddMinutes(5)
            };
        await fixture.Context.SaveChangesAsync();
    }

    /// <summary>A store listed in Explorar that takes online orders every day, with its drawer open.</summary>
    private static async Task<Tenant> StoreAsync(SalesTestFixture fixture, string slug, string name)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Retail, TenantCapability.Retail, slug: slug);
        tenant.Update(name, $"{slug}@demo.com", null, null);
        tenant.SetDirectoryProfile(BusinessCategory.Stationery, null);
        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
        settings.SetOpeningHours(OpeningHours.Create(Enum.GetValues<DayOfWeek>()
            .Select(d => new OpeningInterval(d, TimeOnly.MinValue, new TimeOnly(23, 59)))));
        await fixture.AddProductAsync(tenant, "Cuaderno", 45m);
        var branch = await fixture.Context.Branches.IgnoreQueryFilters().SingleAsync(b => b.TenantId == tenant.Id && b.IsDefault);
        await fixture.OpenCashSessionAsync(tenant, branch.Id);
        return tenant;
    }

    private static Task<PublicMenuDto?> MenuAsync(SalesTestFixture fixture, string slug, string? table = null)
        => new GetPublicMenuQueryHandler(
                fixture.Context, fixture.TenantService, fixture.Clock, FakeOnlinePaymentGateway.Off, new PublicDirectoryCache(),
                NullLogger<GetPublicMenuQueryHandler>.Instance)
            .Handle(new GetPublicMenuQuery(slug, table), CancellationToken.None);

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

    /// <summary>
    /// The read of the ready orders fails, as a database that does not answer would: the one that joins the orders with rows of
    /// their history alone. Every other read works, the one of how soon it confirms them too.
    /// </summary>
    private sealed class FailingReadyRead : IQueryExpressionInterceptor
    {
        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
            => new JoinsRowsOfTheHistory().Finds(queryExpression)
                ? throw new InvalidOperationException("La base no responde.")
                : queryExpression;
    }

    /// <summary>The read of how soon it confirms them fails: one that reads the history but does not join its rows alone.</summary>
    private sealed class FailingConfirmRead : IQueryExpressionInterceptor
    {
        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
            => new Reads(typeof(OrderStatusHistory)).Finds(queryExpression) && !new JoinsRowsOfTheHistory().Finds(queryExpression)
                ? throw new InvalidOperationException("La base no responde.")
                : queryExpression;
    }

    /// <summary>A join whose inner side reads the orders' history and not the orders: the read of the ready orders.</summary>
    private sealed class JoinsRowsOfTheHistory : ExpressionVisitor
    {
        private bool _found;

        public bool Finds(Expression expression)
        {
            Visit(expression);
            return _found;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(Queryable) && node.Method.Name == nameof(Queryable.Join)
                && new Reads(typeof(OrderStatusHistory)).Finds(node.Arguments[1]) && !new Reads(typeof(Order)).Finds(node.Arguments[1]))
                _found = true;
            return base.VisitMethodCall(node);
        }
    }

    private static Task<PublicDirectorySnapshot> SnapshotAsync(SalesTestFixture fixture)
        => PublicDirectorySnapshot.BuildAsync(fixture.Context, fixture.Clock.UtcNow, CancellationToken.None);

    private static PublicDirectorySnapshot.Entry Entry(PublicDirectorySnapshot snapshot, Tenant tenant)
        => snapshot.Entries.Single(entry => entry.TenantId == tenant.Id);
}
