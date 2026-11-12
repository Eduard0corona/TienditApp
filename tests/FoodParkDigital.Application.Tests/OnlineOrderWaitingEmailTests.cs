using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.WaitingNotices;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static FoodParkDigital.Application.Tests.Support.NoticeEmailsSupport;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The reminder of an order of the online menu still waiting to be accepted 15 minutes after it arrived (Fase 31K, ADR 0091):
/// once, until it has waited half an hour, in its own business only; one email per member who gets those of the online orders
/// (Fase 30B) and may confirm them; what it says and never says, and when it is no longer sent.
/// </summary>
public class OnlineOrderWaitingEmailTests
{
    [Fact]
    public async Task ThePlanner_ShouldRemindOnce_TheOrdersOfItsBusinessThatWaitedFifteenMinutes_UntilTheHalfHour()
    {
        await using var fixture = SalesTestFixture.Create();
        var (kitchen, tacos) = await KitchenAsync(fixture, "cocina", "Cocina Luna");
        var (other, otherTacos) = await KitchenAsync(fixture, "otra", "Cocina Otra");
        var waiting = await PlaceAsync(fixture, tacos, kitchen.Slug);
        var accepted = await PlaceAsync(fixture, tacos, kitchen.Slug);
        await ChangeAsync(fixture, accepted.Id, o => o.TransitionTo(OrderStatus.Confirmed, OrderStateMachineFactory.Create(o.BusinessType, o.OrderType), "dueña"));
        var tooOld = await PlaceAsync(fixture, tacos, kitchen.Slug);
        await ChangeAsync(fixture, tooOld.Id, o => o.CreatedAt = fixture.Clock.UtcNow.AddMinutes(-30));
        var theirs = await PlaceAsync(fixture, otherTacos, other.Slug);
        var arrived = fixture.Clock.UtcNow;

        Assert.Equal(0, await RemindAsync(fixture, kitchen, arrived.AddMinutes(15).AddTicks(-1)));
        Assert.Equal(1, await RemindAsync(fixture, kitchen, arrived.AddMinutes(15)));
        Assert.Equal(arrived.AddMinutes(15), await RemindedAtAsync(fixture, waiting));
        Assert.Null(await RemindedAtAsync(fixture, accepted));
        Assert.Null(await RemindedAtAsync(fixture, tooOld));
        Assert.Null(await RemindedAtAsync(fixture, theirs));

        // Once: a later pass reminds nothing more; and the other business's, in its own pass.
        Assert.Equal(0, await RemindAsync(fixture, kitchen, arrived.AddMinutes(20)));
        Assert.Equal(1, await RemindAsync(fixture, other, arrived.AddMinutes(20)));
        Assert.Equal(arrived.AddMinutes(20), await RemindedAtAsync(fixture, theirs));
    }

    [Fact]
    public async Task TheEmails_ShouldGoToWhoGetsThoseOfTheOnlineOrders_AndMayConfirmThem_OnePerPerson()
    {
        await using var fixture = SalesTestFixture.Create();
        var (kitchen, tacos) = await KitchenAsync(fixture, "cocina", "Cocina Luna");
        var owner = await MemberAsync(fixture, kitchen, "dueña", TenantUserRole.Owner);
        await MemberAsync(fixture, kitchen, "socia", TenantUserRole.Owner, chooseOrderCancellations: false);
        var cashier = await MemberAsync(fixture, kitchen, "cajero", TenantUserRole.Staff, permissions: [TenantPermission.ManageOrders], chooseOrderCancellations: true);
        // Chose them, but may not confirm the orders; may confirm them, but did not choose them; chose the reviews only.
        await MemberAsync(fixture, kitchen, "estilista", TenantUserRole.Staff, permissions: [TenantPermission.ManageAppointments], chooseOrderCancellations: true);
        await MemberAsync(fixture, kitchen, "mesero", TenantUserRole.Staff, permissions: [TenantPermission.ManageOrders]);
        await MemberAsync(fixture, kitchen, "admin", TenantUserRole.Admin, chooseReviews: true);
        var (other, _) = await KitchenAsync(fixture, "otra", "Cocina Otra");
        await MemberAsync(fixture, other, "otra", TenantUserRole.Owner);
        var order = await PlaceAsync(fixture, tacos, kitchen.Slug);

        fixture.TenantService.SetTenant(kitchen.Id);
        var outbox = new Outbox<OnlineOrderWaitingEmailEvent>();
        await new OnlineOrderWaitingEmailsHandler(fixture.Context, new MembersAccess(fixture.Context), outbox)
            .HandleAsync(new OnlineOrderWaitingEvent(order.Id, kitchen.Id), Context(kitchen), CancellationToken.None);

        Assert.Equal(new[] { owner, cashier }.Order(), outbox.Added.Select(q => q.Event.UserId).Order());
        Assert.All(outbox.Added, q => Assert.Equal((order.Id, (Guid?)kitchen.Id), (q.Event.OrderId, q.TenantId)));
    }

    [Fact]
    public async Task TheEmail_ShouldSayWhoWaitsForWhichOrder_AndForHowMuch_NeverTheirPhone()
    {
        await using var fixture = SalesTestFixture.Create();
        var (kitchen, tacos) = await KitchenAsync(fixture, "cocina", "Cocina <Luna>");
        var owner = await MemberAsync(fixture, kitchen, "dueña", TenantUserRole.Owner);
        var order = await PlaceAsync(fixture, tacos, kitchen.Slug, customer: "<b>Mariana</b>");

        var (to, subject, body) = Assert.Single(await SendAsync(fixture, kitchen, order, owner));

        Assert.Equal($"dueña.{kitchen.Slug}@demo.com", to);
        Assert.Equal($"Pedido en línea por confirmar en Cocina <Luna>: {order.OrderNumber} por $120.00", subject);
        Assert.Contains($"<strong>&lt;b&gt;Mariana&lt;/b&gt;</strong> pidió en línea a Cocina &lt;Luna&gt; el pedido <strong>{order.OrderNumber}</strong>, por un total de <strong>$120.00</strong>, y lleva más de 15 minutos esperando a que lo acepten o lo rechacen.", body);
        Assert.Contains("antes de que cumpla 30 minutos", body);
        Assert.Contains($"<a href='{AppUrl}/orders'>Abrir Pedidos</a>", body);
        Assert.Contains($"<a href='{AppUrl}/profile'>Mi perfil</a>", body);
        Assert.DoesNotContain("<b>", body);
        Assert.DoesNotContain("1234", body);
        Assert.DoesNotContain("Orden de tacos", body);
    }

    [Fact]
    public async Task TheEmail_ShouldNotBeSent_OnceTheOrderWasAnswered_OrThePersonNoLongerGetsThemOrMayConfirmThem()
    {
        await using var fixture = SalesTestFixture.Create();
        var (kitchen, tacos) = await KitchenAsync(fixture, "cocina", "Cocina Luna");
        var owner = await MemberAsync(fixture, kitchen, "dueña", TenantUserRole.Owner);
        var cashier = await MemberAsync(fixture, kitchen, "cajero", TenantUserRole.Staff, permissions: [TenantPermission.ManageOrders], chooseOrderCancellations: true);
        var accepted = await PlaceAsync(fixture, tacos, kitchen.Slug);
        await ChangeAsync(fixture, accepted.Id, o => o.TransitionTo(OrderStatus.Confirmed, OrderStateMachineFactory.Create(o.BusinessType, o.OrderType), "dueña"));
        var cancelled = await PlaceAsync(fixture, tacos, kitchen.Slug);
        await ChangeAsync(fixture, cancelled.Id, o => o.CancelByCustomer(OrderStateMachineFactory.Create(o.BusinessType, o.OrderType)));
        var waiting = await PlaceAsync(fixture, tacos, kitchen.Slug);

        Assert.Empty(await SendAsync(fixture, kitchen, accepted, owner));
        Assert.Empty(await SendAsync(fixture, kitchen, cancelled, owner));
        Assert.Single(await SendAsync(fixture, kitchen, waiting, cashier));

        // They stopped getting them, or lost the permission to confirm orders, since it was queued.
        var membership = await fixture.Context.TenantUsers.IgnoreQueryFilters().SingleAsync(m => m.UserId == cashier);
        membership.SetExplicitPermissions([nameof(TenantPermission.ManageAppointments)]);
        var ownership = await fixture.Context.TenantUsers.IgnoreQueryFilters().SingleAsync(m => m.UserId == owner);
        ownership.ChooseOrderCancellationEmails(false);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        Assert.Empty(await SendAsync(fixture, kitchen, waiting, cashier));
        Assert.Empty(await SendAsync(fixture, kitchen, waiting, owner));
    }

    [Fact]
    public async Task AFailingProvider_ShouldThrow_SoTheOutboxRetriesThatEmailAlone()
    {
        await using var fixture = SalesTestFixture.Create();
        var (kitchen, tacos) = await KitchenAsync(fixture, "cocina", "Cocina Luna");
        var owner = await MemberAsync(fixture, kitchen, "dueña", TenantUserRole.Owner);
        var order = await PlaceAsync(fixture, tacos, kitchen.Slug);

        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(fixture, kitchen, order, owner, failing: true));
    }

    /// <summary>A kitchen that takes online orders and accepts each one itself: they arrive waiting.</summary>
    private static async Task<(Tenant Tenant, Product Tacos)> KitchenAsync(SalesTestFixture fixture, string slug, string name)
    {
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, TenantCapability.Food, pricesIncludeTax: true, slug: slug);
        tenant.Update(name, $"{slug}@demo.com", null, null);
        var tacos = await fixture.AddProductAsync(tenant, "Orden de tacos", 120m);
        var settings = await fixture.Context.TenantSettings.IgnoreQueryFilters().SingleAsync(s => s.TenantId == tenant.Id);
        settings.UpdateOnlineMenu(OnlineMenuMode.Orders);
        settings.Update(settings.Currency, settings.TimeZone, autoAcceptOrders: false, settings.MaxConcurrentOrders);
        await fixture.Context.SaveChangesAsync();
        await fixture.OpenCashSessionAsync(tenant, (await fixture.Context.Branches.IgnoreQueryFilters().SingleAsync(b => b.TenantId == tenant.Id && b.IsDefault)).Id);
        fixture.Context.ChangeTracker.Clear();
        return (tenant, tacos);
    }

    private static async Task<Order> PlaceAsync(SalesTestFixture fixture, Product product, string slug, string customer = "Mariana Ortiz")
    {
        var phones = new PhoneNormalizationService();
        var lookup = new CustomerLookupService(fixture.Context, phones, NullLogger<CustomerLookupService>.Instance);
        var placed = await new PlacePublicOrderCommandHandler(
                fixture.Context, fixture.TenantService, lookup, phones, fixture.Clock, NoOpOrderInventoryService.Instance,
                new FolioGenerator(fixture.Context), fixture.CurrentUser)
            .Handle(
                new PlacePublicOrderCommand
                {
                    Slug = slug,
                    CustomerName = customer,
                    CustomerPhone = "55 1234 5678",
                    Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = 1 }]
                },
                CancellationToken.None);
        Assert.True(placed.IsSuccess, placed.Error);
        fixture.Context.ChangeTracker.Clear();
        var order = await fixture.Context.Orders.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.TrackingToken == placed.Value.TrackingToken);
        Assert.Equal(OrderStatus.Pending, order.Status);
        // The fixture has no auditing interceptor: the order was placed now, as the database would stamp it.
        await ChangeAsync(fixture, order.Id, o => o.CreatedAt = fixture.Clock.UtcNow);
        return order;
    }

    private static async Task ChangeAsync(SalesTestFixture fixture, Guid orderId, Action<Order> change)
    {
        var order = await fixture.Context.Orders.IgnoreQueryFilters().Include(o => o.StatusHistory).SingleAsync(o => o.Id == orderId);
        change(order);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    /// <summary>One pass of the planner in <paramref name="tenant"/>'s scope at <paramref name="at"/>, saved as the worker does.</summary>
    private static async Task<int> RemindAsync(SalesTestFixture fixture, Tenant tenant, DateTimeOffset at)
    {
        var now = fixture.Clock.UtcNow;
        fixture.Clock.UtcNow = at;
        fixture.TenantService.SetTenant(tenant.Id);
        var reminded = await new OnlineOrderWaitingPlanner(fixture.Context, fixture.Clock).RemindDueAsync(CancellationToken.None);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        fixture.Clock.UtcNow = now;
        return reminded;
    }

    private static Task<DateTimeOffset?> RemindedAtAsync(SalesTestFixture fixture, Order order)
        => fixture.Context.Orders.IgnoreQueryFilters().AsNoTracking().Where(o => o.Id == order.Id).Select(o => o.ConfirmReminderSentAt).SingleAsync();

    private static async Task<List<(string To, string Subject, string Body)>> SendAsync(
        SalesTestFixture fixture, Tenant tenant, Order order, Guid userId, bool failing = false)
    {
        fixture.TenantService.SetTenant(tenant.Id);
        var emails = new Emails(fail: failing);
        await new OnlineOrderWaitingEmailHandler(fixture.Context, new MembersAccess(fixture.Context), emails, new Links(AppUrl))
            .HandleAsync(new OnlineOrderWaitingEmailEvent(order.Id, userId), Context(tenant), CancellationToken.None);
        return emails.Sent;
    }
}
