using System.Linq.Expressions;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.PublicDirectory;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Appointments.Entities;
using FoodParkDigital.Domain.Appointments.OnlineBooking;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging.Abstractions;
using static FoodParkDigital.Application.Tests.ExploreResponseTimeTests;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// How soon a business confirms the orders its customers place on its online menu (Fase 29T, ADR 0074): from when each
/// arrived until the business first accepted or declined it, in its card in Explorar, its menu and for its owner.
/// </summary>
public class OrderConfirmTimeInExploreTests
{
    private static readonly IOrderStateMachine Kitchen = OrderStateMachineFactory.Create(BusinessType.Food, OrderType.Retail);
    private static readonly DayOfWeek[] EveryDay = Enum.GetValues<DayOfWeek>();

    [Fact]
    public async Task ACard_ShouldSayWithinWhatItConfirmedHalfItsOrders_DeclinedOnesToo_FromThreeOrders()
    {
        await using var fixture = Fixture();
        var tacos = await KitchenAsync(fixture, "tacos", "Tacos El Güero");
        // Declined after 3 minutes, accepted after 10 and after 20: half within 15 minutes.
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(3), declined: true);
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(10));
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(20));
        var pizza = await KitchenAsync(fixture, "pizza", "Pizzería Napoli");
        await OrderAsync(fixture, pizza, answeredAfter: TimeSpan.FromMinutes(1));
        await OrderAsync(fixture, pizza, answeredAfter: TimeSpan.FromMinutes(1));

        var cards = await CardsAsync(fixture);

        Assert.Equal(OrderConfirmTime.WithinFifteenMinutes, cards["tacos"].OrderConfirmTime);
        // Two orders say nothing.
        Assert.Null(cards["pizza"].OrderConfirmTime);
    }

    [Fact]
    public async Task OnlyTheOrdersOfItsOnlineMenu_ShouldCount_OncePerCustomer_InTheLast90Days()
    {
        await using var fixture = Fixture();
        var tacos = await KitchenAsync(fixture, "tacos", "Tacos El Güero");
        // Three customers confirmed after 40 minutes: slower than half an hour, which the card does not say.
        var phone = NewCustomerPhone();
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(40), phone: phone, arrivedDaysAgo: 30);
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(40));
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(40));
        // None of these counts: one ordered with the QR of a table, one the team took at the counter or from WhatsApp, one
        // the team registered as online, one that arrived more than 90 days ago, and the orders of another business.
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(1), table: "4");
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(1), channel: OrderChannel.Counter);
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(1), channel: OrderChannel.WhatsApp);
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(1), byTheTeam: true);
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(1), arrivedDaysAgo: 100);
        var pizza = await KitchenAsync(fixture, "pizza", "Pizzería Napoli");
        for (var n = 0; n < 3; n++)
            await OrderAsync(fixture, pizza, answeredAfter: TimeSpan.FromMinutes(1));

        var before = await SnapshotAsync(fixture);
        Assert.Equal(OrderConfirmTime.Slower, Entry(before, tacos).OrderConfirmTime);
        Assert.Null(Entry(before, tacos).Business.OrderConfirmTime);
        Assert.Equal(OrderConfirmTime.WithinFiveMinutes, Entry(before, pizza).OrderConfirmTime);

        // The first customer orders again and is confirmed at once: their last order is the one that counts, so two of three
        // are still slower.
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(1), phone: phone, arrivedDaysAgo: 2);
        Assert.Equal(OrderConfirmTime.Slower, Entry(await SnapshotAsync(fixture), tacos).OrderConfirmTime);
        // A fourth customer confirmed at once: two of four within five minutes is half.
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(1));
        Assert.Equal(OrderConfirmTime.WithinFiveMinutes, Entry(await SnapshotAsync(fixture), tacos).OrderConfirmTime);
        // The first customer orders once more, still waiting for 10 minutes, and once more they cancel after 2: neither is
        // decided, so their quick order still counts.
        await OrderAsync(fixture, tacos, arrivedAt: Now.AddMinutes(-10), phone: phone);
        await OrderAsync(fixture, tacos, cancelledByCustomerAfter: TimeSpan.FromMinutes(2), arrivedAt: Now.AddMinutes(-5), phone: phone);
        Assert.Equal(OrderConfirmTime.WithinFiveMinutes, Entry(await SnapshotAsync(fixture), tacos).OrderConfirmTime);
    }

    [Fact]
    public async Task ACustomer_ShouldCountByTheirLastOrder_ThoughAnEarlierOneWasAsQuick()
    {
        await using var fixture = Fixture();
        var tacos = await KitchenAsync(fixture, "tacos", "Tacos El Güero");
        // A customer confirmed at once, then after 40 minutes, then at once again: the last one is quick. Since the Fase 30E
        // each tramo says when its last order arrived, and the one of the latest is the one that counts (not the tramo
        // whose first order arrived last, which would be the slow one).
        var phone = NewCustomerPhone();
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(1), phone: phone, arrivedDaysAgo: 10);
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(40), phone: phone, arrivedDaysAgo: 5);
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(1), phone: phone, arrivedDaysAgo: 1);
        // Two more customers: one confirmed at once, one after 40 minutes.
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(1));
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(40));

        // Two of three within five minutes.
        Assert.Equal(OrderConfirmTime.WithinFiveMinutes, Entry(await SnapshotAsync(fixture), tacos).OrderConfirmTime);
    }

    [Fact]
    public async Task TwoOrdersOfACustomerAtOnce_ShouldCountOnce_AsTheSlowerOfThem()
    {
        await using var fixture = Fixture();
        var tacos = await KitchenAsync(fixture, "tacos", "Tacos El Güero");
        // Two orders of one phone that arrived at the same moment, confirmed after 1 minute and after 40: since the Fase 30E
        // the customer counts once, as the slower (before, both counted), as in the other times of Explorar.
        var phone = NewCustomerPhone();
        var atOnce = Now.AddDays(-2);
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(1), phone: phone, arrivedAt: atOnce);
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(40), phone: phone, arrivedAt: atOnce);
        // Two more customers: one confirmed after 1 minute, one after 40.
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(1));
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(40));

        // One of three within five minutes (counting both, two of four would be half).
        Assert.Equal(OrderConfirmTime.Slower, Entry(await SnapshotAsync(fixture), tacos).OrderConfirmTime);
    }

    [Fact]
    public async Task AnOrderAcceptedAsItArrived_ShouldCount_AtTheEdgeOfThe90Days()
    {
        await using var fixture = Fixture();
        var tacos = await KitchenAsync(fixture, "tacos", "Tacos El Güero");
        // Accepted automatically as it arrived, a moment after the 90 days began: its row of history is stamped a few
        // milliseconds before the order is saved, so before the 90 days began. It is still the quick one it was.
        var since = OrderConfirmTimes.Since(Now);
        await OrderAsync(fixture, tacos, automatically: true, arrivedAt: since.AddMilliseconds(40));
        var answer = await fixture.Context.OrderStatusHistories.IgnoreQueryFilters()
            .SingleAsync(h => h.TenantId == tacos.Id && h.PreviousStatus == OrderStatus.Pending && h.NewStatus != OrderStatus.Pending);
        fixture.Context.Entry(answer).Property(h => h.ChangedAt).CurrentValue = since.AddMilliseconds(-40);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(1));
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(40));

        // Two of three within five minutes.
        Assert.Equal(OrderConfirmTime.WithinFiveMinutes, Entry(await SnapshotAsync(fixture), tacos).OrderConfirmTime);
    }

    [Fact]
    public async Task EachBusiness_ShouldCountItsOwnCustomers_ThoughTheyOrderAtAnotherOneLater()
    {
        await using var fixture = Fixture();
        var tacos = await KitchenAsync(fixture, "tacos", "Tacos El Güero");
        var pizza = await KitchenAsync(fixture, "pizza", "Pizzería Napoli");
        // The same three customers order tacos, confirmed at once, and pizza two days later, confirmed after 40 minutes.
        foreach (var phone in new[] { NewCustomerPhone(), NewCustomerPhone(), NewCustomerPhone() })
        {
            await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(1), phone: phone, arrivedDaysAgo: 5);
            await OrderAsync(fixture, pizza, answeredAfter: TimeSpan.FromMinutes(40), phone: phone, arrivedDaysAgo: 3);
        }

        var snapshot = await SnapshotAsync(fixture);

        Assert.Equal(OrderConfirmTime.WithinFiveMinutes, Entry(snapshot, tacos).OrderConfirmTime);
        Assert.Equal(OrderConfirmTime.Slower, Entry(snapshot, pizza).OrderConfirmTime);
    }

    [Fact]
    public async Task OrdersWithoutAnAnswer_ShouldCountAsTheSlowest_OnceTheyWaitedHalfAnHour()
    {
        await using var fixture = Fixture();
        var tacos = await KitchenAsync(fixture, "tacos", "Tacos El Güero");
        // Quick: one accepted automatically as it arrived, and one accepted after 2 minutes.
        await OrderAsync(fixture, tacos, automatically: true);
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(2));
        // The slowest: one still waiting after 40 minutes, and one its customer cancelled after waiting 35.
        await OrderAsync(fixture, tacos, arrivedAt: Now.AddMinutes(-40));
        await OrderAsync(fixture, tacos, cancelledByCustomerAfter: TimeSpan.FromMinutes(35));
        // Not yet, or never: one waiting for 10 minutes, and one its customer cancelled after 10.
        await OrderAsync(fixture, tacos, arrivedAt: Now.AddMinutes(-10));
        await OrderAsync(fixture, tacos, cancelledByCustomerAfter: TimeSpan.FromMinutes(10));

        var now = await SnapshotAsync(fixture);
        fixture.Clock.UtcNow = Now.AddMinutes(25);
        var later = await SnapshotAsync(fixture);

        // Two of four within five minutes; 25 minutes later, the one still waiting has waited 35: two of five.
        Assert.Equal(OrderConfirmTime.WithinFiveMinutes, Entry(now, tacos).OrderConfirmTime);
        Assert.Equal(OrderConfirmTime.Slower, Entry(later, tacos).OrderConfirmTime);
    }

    [Fact]
    public async Task TheLimits_ShouldLeaveOut_AWaitOfExactlyFiveMinutes()
    {
        await using var fixture = Fixture();
        var tacos = await KitchenAsync(fixture, "tacos", "Tacos El Güero");
        // Within five minutes, exactly five, exactly fifteen, and more than half an hour: half is within 15, not five.
        foreach (var minutes in new[] { 4, 5, 15, 31 })
            await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(minutes));

        Assert.Equal(OrderConfirmTime.WithinFifteenMinutes, Entry(await SnapshotAsync(fixture), tacos).OrderConfirmTime);
    }

    [Fact]
    public async Task HalfAnHourExactly_ShouldBeAmongTheSlowest_AnsweredOrStillWaiting()
    {
        await using var fixture = Fixture();
        var tacos = await KitchenAsync(fixture, "tacos", "Tacos El Güero");
        // Answered after 20 minutes and after exactly 30, and one waiting for exactly 30 minutes now: two of three slower.
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(20));
        await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(30));
        await OrderAsync(fixture, tacos, arrivedAt: Now.AddMinutes(-30));

        Assert.Equal(OrderConfirmTime.Slower, Entry(await SnapshotAsync(fixture), tacos).OrderConfirmTime);
    }

    [Fact]
    public async Task ASnapshotThatCouldNotReadTheOrders_ShouldNotTellTheOwnerTheyAreNotCountedYet()
    {
        await using var fixture = SalesTestFixture.Create(null, new FailingOrderHistory());
        fixture.Clock.UtcNow = Now;
        var tacos = await KitchenAsync(fixture, "tacos", "Tacos El Güero");
        for (var n = 0; n < 3; n++)
            await OrderAsync(fixture, tacos, answeredAfter: TimeSpan.FromMinutes(1));

        var profile = await ProfileAsync(fixture, tacos);
        var card = (await CardsAsync(fixture))["tacos"];

        Assert.Equal((false, (OrderConfirmTime?)null), (profile.CountsOrders, profile.OrderConfirmTime));
        Assert.Null(card.OrderConfirmTime);
    }

    [Fact]
    public async Task ASnapshotThatCouldNotReadTheRequests_ShouldStillSayHowSoonItConfirms_EachFromItsOwnRead()
    {
        await using var fixture = SalesTestFixture.Create(null, new FailingAppointments());
        fixture.Clock.UtcNow = Now;
        // A salon that books online and also takes online orders.
        var salon = await SalonAsync(fixture, "luna", "Estética Luna", product: true);
        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == salon.Id);
        settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
        settings.Update(settings.Currency, settings.TimeZone, autoAcceptOrders: false, settings.MaxConcurrentOrders);
        var branch = await fixture.Context.Branches.IgnoreQueryFilters().SingleAsync(b => b.TenantId == salon.Id && b.IsDefault);
        await fixture.OpenCashSessionAsync(salon, branch.Id);
        for (var n = 0; n < 3; n++)
            await OrderAsync(fixture, salon, answeredAfter: TimeSpan.FromMinutes(2));

        var profile = await ProfileAsync(fixture, salon);
        var menu = await MenuAsync(fixture, "luna");

        // Its requests could not be read: nothing of them, not even «not yet»; its orders could, and say how soon.
        Assert.Equal((false, (ResponseTime?)null), (profile.CountsRequests, profile.ResponseTime));
        Assert.Equal((true, (OrderConfirmTime?)OrderConfirmTime.WithinFiveMinutes), (profile.CountsOrders, profile.OrderConfirmTime));
        Assert.Equal(OrderConfirmTime.WithinFiveMinutes, menu!.OrderConfirmTime);
        Assert.Null(menu.ResponseTime);
    }

    [Fact]
    public async Task TheOwner_ShouldReadHowSoonItConfirms_TheSlowerToo_AndItsMenuWhatItsCardSays()
    {
        await using var fixture = Fixture();
        var slow = await KitchenAsync(fixture, "tacos", "Tacos El Güero");
        for (var n = 0; n < 3; n++)
            await OrderAsync(fixture, slow, answeredAfter: TimeSpan.FromMinutes(40));
        var quick = await KitchenAsync(fixture, "pizza", "Pizzería Napoli");
        for (var n = 0; n < 3; n++)
            await OrderAsync(fixture, quick, answeredAfter: TimeSpan.FromMinutes(2));
        var newer = await KitchenAsync(fixture, "sushi", "Sushi Koi");
        // A menu that takes no orders (any more): neither its card nor its owner says anything of them.
        var menuOnly = await KitchenAsync(fixture, "cafe", "Café Luna", ordersOn: false);
        for (var n = 0; n < 3; n++)
            await OrderAsync(fixture, menuOnly, answeredAfter: TimeSpan.FromMinutes(2));
        Assert.Null(Entry(await SnapshotAsync(fixture), menuOnly).OrderConfirmTime);

        var slowProfile = await ProfileAsync(fixture, slow);
        Assert.Equal((true, OrderConfirmTime.Slower), (slowProfile.CountsOrders, slowProfile.OrderConfirmTime));
        Assert.Equal(OrderConfirmTime.WithinFiveMinutes, (await ProfileAsync(fixture, quick)).OrderConfirmTime);
        var newerProfile = await ProfileAsync(fixture, newer);
        Assert.Equal((true, (OrderConfirmTime?)null), (newerProfile.CountsOrders, newerProfile.OrderConfirmTime));
        var menuOnlyProfile = await ProfileAsync(fixture, menuOnly);
        Assert.False(menuOnlyProfile.CountsOrders);
        // A kitchen takes no requests: nothing of them either.
        Assert.False(slowProfile.CountsRequests);

        var cards = await CardsAsync(fixture);
        Assert.Null(cards["tacos"].OrderConfirmTime);
        Assert.Equal(OrderConfirmTime.WithinFiveMinutes, cards["pizza"].OrderConfirmTime);
        // Its menu says what its card says, but not at a table, where whoever serves it is there.
        Assert.Null((await MenuAsync(fixture, "tacos"))!.OrderConfirmTime);
        Assert.Equal(OrderConfirmTime.WithinFiveMinutes, (await MenuAsync(fixture, "pizza"))!.OrderConfirmTime);
        Assert.Null((await MenuAsync(fixture, "pizza", table: "4"))!.OrderConfirmTime);
    }

    /// <summary>
    /// A kitchen listed in Explorar that takes online orders every day, accepting each one itself, with its drawer open; or
    /// that only shows its menu.
    /// </summary>
    internal static async Task<Tenant> KitchenAsync(
        SalesTestFixture fixture, string slug, string name, bool ordersOn = true, bool drawerOpen = true)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, slug: slug);
        tenant.Update(name, $"{slug}@demo.com", null, null);
        tenant.SetDirectoryProfile(BusinessCategory.Food, null);
        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineMenu(ordersOn ? OnlineMenuMode.Orders : OnlineMenuMode.MenuOnly);
        settings.Update(settings.Currency, settings.TimeZone, autoAcceptOrders: false, settings.MaxConcurrentOrders);
        settings.SetOpeningHours(OpeningHours.Create(EveryDay.Select(d => new OpeningInterval(d, TimeOnly.MinValue, new TimeOnly(23, 59)))));
        await fixture.AddProductAsync(tenant, "Orden de tacos", 120m);
        // Without its drawer open, its orders are paused (Fase 29Y).
        if (drawerOpen)
        {
            var branch = await fixture.Context.Branches.IgnoreQueryFilters().SingleAsync(b => b.TenantId == tenant.Id && b.IsDefault);
            await fixture.OpenCashSessionAsync(tenant, branch.Id);
        }
        return tenant;
    }

    private static int _folio;

    /// <summary>
    /// An order its customer placed on the online menu <paramref name="arrivedDaysAgo"/> (or <paramref name="arrivedAt"/>),
    /// still waiting unless the business answered it after <paramref name="answeredAfter"/>, accepted it automatically, or its
    /// customer cancelled it after <paramref name="cancelledByCustomerAfter"/>.
    /// </summary>
    internal static async Task OrderAsync(
        SalesTestFixture fixture, Tenant tenant, TimeSpan? answeredAfter = null, bool declined = false, bool automatically = false,
        TimeSpan? cancelledByCustomerAfter = null, int arrivedDaysAgo = 3, DateTimeOffset? arrivedAt = null, string? phone = null,
        string? table = null, OrderChannel channel = OrderChannel.Online, bool byTheTeam = false)
    {
        var arrived = arrivedAt ?? Now.AddDays(-arrivedDaysAgo);
        var order = new Order(
            tenant.Id, null, byTheTeam ? null : Guid.NewGuid(), byTheTeam, $"P-{Interlocked.Increment(ref _folio)}", BusinessType.Food,
            OrderType.Retail, "Valeria", phone ?? NewCustomerPhone(), null, table, channel: channel)
        {
            CreatedAt = arrived
        };
        DateTimeOffset? changedAt = null;
        if (automatically)
        {
            order.AdvanceOnCreation(OrderStatus.Confirmed, Kitchen, "menu-en-linea");
            changedAt = arrived;
        }
        else if (answeredAfter is { } after)
        {
            if (declined)
                order.Cancel(Kitchen, "cajero@demo.com", "Se acabó el pastor.");
            else
                order.TransitionTo(OrderStatus.Confirmed, Kitchen, "cajero@demo.com");
            changedAt = arrived + after;
        }
        else if (cancelledByCustomerAfter is { } cancelled)
        {
            order.CancelByCustomer(Kitchen);
            changedAt = arrived + cancelled;
        }

        fixture.Context.Orders.Add(order);
        // The history keeps when each change happened; the first row, as the order was created, when it arrived.
        foreach (var row in order.StatusHistory)
            fixture.Context.Entry(row).Property(h => h.ChangedAt).CurrentValue =
                row.NewStatus == row.PreviousStatus ? arrived : changedAt!.Value;
        await fixture.Context.SaveChangesAsync();
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

    /// <summary>The read of the appointments fails, as a database that does not answer would; every other read works.</summary>
    private sealed class FailingAppointments : IQueryExpressionInterceptor
    {
        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
            => new Reads(typeof(Appointment)).Finds(queryExpression)
                ? throw new InvalidOperationException("La base no responde.")
                : queryExpression;
    }

    /// <summary>Whether a query reads the entity of <paramref name="type"/>.</summary>
    internal sealed class Reads(Type type) : ExpressionVisitor
    {
        private bool _found;

        public bool Finds(Expression expression)
        {
            Visit(expression);
            return _found;
        }

        public override Expression? Visit(Expression? node)
        {
            if (node is QueryRootExpression root && root.ElementType == type)
                _found = true;
            return base.Visit(node);
        }
    }

    /// <summary>The read of the orders' history fails, as a database that does not answer would; every other read works.</summary>
    private sealed class FailingOrderHistory : IQueryExpressionInterceptor
    {
        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
            => new Reads(typeof(OrderStatusHistory)).Finds(queryExpression)
                ? throw new InvalidOperationException("La base no responde.")
                : queryExpression;
    }

    private static Task<PublicDirectorySnapshot> SnapshotAsync(SalesTestFixture fixture)
        => PublicDirectorySnapshot.BuildAsync(fixture.Context, fixture.Clock.UtcNow, CancellationToken.None);

    private static PublicDirectorySnapshot.Entry Entry(PublicDirectorySnapshot snapshot, Tenant tenant)
        => snapshot.Entries.Single(entry => entry.TenantId == tenant.Id);
}
