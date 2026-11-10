using System.Net;
using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.CancellationNotices;
using FoodParkDigital.Application.Orders.Commands;
using FoodParkDigital.Application.Orders.EventHandlers;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using FoodParkDigital.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static FoodParkDigital.Application.Tests.Support.NoticeEmailsSupport;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The email of an online order its customer cancelled before the business accepted it (Fase 30B, ADR 0082), or the business
/// cancelled because its customer asked (Fase 31B, ADR 0084): one per member who gets them and may confirm orders, but not to
/// whoever cancelled it, what it says and never says, when it is no longer sent, and each person's own choice.
/// </summary>
public class OrderCancellationEmailTests
{
    [Fact]
    public async Task TheEmails_ShouldGoToWhoGetsThem_AndMayConfirmTheOrders_OnePerPerson()
    {
        await using var fixture = SalesTestFixture.Create();
        var (kitchen, tacos) = await KitchenAsync(fixture, "cocina", "Cocina Luna");
        var owner = await MemberAsync(fixture, kitchen, "dueña", TenantUserRole.Owner);
        await MemberAsync(fixture, kitchen, "socia", TenantUserRole.Owner, chooseOrderCancellations: false);
        var cashier = await MemberAsync(fixture, kitchen, "cajero", TenantUserRole.Staff, permissions: [TenantPermission.ManageOrders], chooseOrderCancellations: true);
        // Chose them, but may not confirm the orders; may confirm them, but did not choose them; chose the others only.
        await MemberAsync(fixture, kitchen, "estilista", TenantUserRole.Staff, permissions: [TenantPermission.ManageAppointments], chooseOrderCancellations: true);
        await MemberAsync(fixture, kitchen, "mesero", TenantUserRole.Staff, permissions: [TenantPermission.ManageOrders]);
        await MemberAsync(fixture, kitchen, "admin", TenantUserRole.Admin, choose: true, chooseQuotes: true);
        // No longer of the team, or with an email nobody confirmed.
        await MemberAsync(fixture, kitchen, "antes", TenantUserRole.Staff, permissions: [TenantPermission.ManageOrders], chooseOrderCancellations: true, status: TenantUserStatus.Disabled);
        await MemberAsync(fixture, kitchen, "nueva", TenantUserRole.Staff, permissions: [TenantPermission.ManageOrders], chooseOrderCancellations: true, emailConfirmed: false);
        var (other, _) = await KitchenAsync(fixture, "otra", "Cocina Otra");
        await MemberAsync(fixture, other, "otra", TenantUserRole.Owner);
        var order = await CancelledByCustomerAsync(fixture, kitchen, tacos);

        var queued = await QueueAsync(fixture, kitchen, order, byCustomer: true);

        Assert.Equal(new[] { owner, cashier }.Order(), queued.Select(q => q.Event.UserId).Order());
        Assert.All(queued, q => Assert.Equal((order.Id, (Guid?)kitchen.Id), (q.Event.OrderId, q.TenantId)));
        // The business cancelled it: nobody needs to be told.
        Assert.Empty(await QueueAsync(fixture, kitchen, order, byCustomer: false));
    }

    [Fact]
    public async Task TheEmail_ShouldSayWhoCancelledWhichOrder_AndForHowMuch_NeverTheirPhone()
    {
        await using var fixture = SalesTestFixture.Create();
        var (kitchen, tacos) = await KitchenAsync(fixture, "cocina", "Cocina <Luna>");
        var owner = await MemberAsync(fixture, kitchen, "dueña", TenantUserRole.Owner);
        var order = await CancelledByCustomerAsync(fixture, kitchen, tacos, customer: "<b>Mariana</b>");

        var (to, subject, body) = Assert.Single(await SendAsync(fixture, kitchen, order, owner));

        Assert.Equal($"dueña.{kitchen.Slug}@demo.com", to);
        Assert.Equal($"Pedido cancelado en Cocina <Luna>: {order.OrderNumber} por $120.00", subject);
        Assert.Contains($"<strong>&lt;b&gt;Mariana&lt;/b&gt;</strong> canceló su pedido en línea <strong>{order.OrderNumber}</strong> de Cocina &lt;Luna&gt;, por un total de <strong>$120.00</strong>, antes de que lo aceptaran.", body);
        Assert.Contains("Ya no tienes que prepararlo.", body);
        Assert.Contains($"<a href='{AppUrl}/orders'>Abrir Pedidos</a>", body);
        Assert.DoesNotContain("<b>", body);
        Assert.DoesNotContain("1234", body);
        Assert.DoesNotContain("Orden de tacos", body);
    }

    [Fact]
    public async Task NoEmail_ShouldGo_ForAnOrderTheBusinessCancelled_OneOverADayOld_OrToWhoNoLongerGetsThem()
    {
        await using var fixture = SalesTestFixture.Create();
        var (kitchen, tacos) = await KitchenAsync(fixture, "cocina", "Cocina Luna");
        var owner = await MemberAsync(fixture, kitchen, "dueña", TenantUserRole.Owner);
        var cashier = await MemberAsync(fixture, kitchen, "cajero", TenantUserRole.Staff, permissions: [TenantPermission.ManageOrders], chooseOrderCancellations: true);
        var byTheBusiness = await PlaceAsync(fixture, tacos, kitchen.Slug);
        await ChangeAsync(fixture, byTheBusiness.Id, o => o.Cancel(OrderStateMachineFactory.Create(o.BusinessType, o.OrderType), "cajero@demo.com", "Sin existencias"));
        var order = await CancelledByCustomerAsync(fixture, kitchen, tacos);

        Assert.Empty(await SendAsync(fixture, kitchen, byTheBusiness, owner));

        // A day after the customer cancelled it, to the minute: still sent. A minute later, whoever confirms orders saw it.
        Assert.Single(await SendAsync(fixture, kitchen, order, owner, after: TimeSpan.FromDays(1)));
        Assert.Empty(await SendAsync(fixture, kitchen, order, owner, after: TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1)));

        // The cashier gets it while they chose it, and not once they turned it off since it was queued.
        Assert.Single(await SendAsync(fixture, kitchen, order, cashier));
        var membership = await fixture.Context.TenantUsers.IgnoreQueryFilters().SingleAsync(m => m.UserId == cashier);
        membership.ChooseOrderCancellationEmails(false);
        await fixture.Context.SaveChangesAsync();
        Assert.Empty(await SendAsync(fixture, kitchen, order, cashier));

        // A business suspended since: none, as the other notices.
        (await fixture.Context.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == kitchen.Id)).SetStatus(TenantStatus.Suspended);
        await fixture.Context.SaveChangesAsync();
        Assert.Empty(await SendAsync(fixture, kitchen, order, owner));
    }

    [Fact]
    public async Task AnOrderCancelledBecauseItsCustomerAsked_ShouldBeTold_ToWhoGetsThem_ButNotWhoCancelledIt()
    {
        // Fase 31B (ADR 0084): accepted, its customer calls, and the cashier cancels it.
        var raised = new RaisedEvents();
        await using var fixture = SalesTestFixture.Create(raised);
        var (kitchen, tacos) = await KitchenAsync(fixture, "cocina", "Cocina Luna");
        var owner = await MemberAsync(fixture, kitchen, "dueña", TenantUserRole.Owner);
        var cashier = await MemberAsync(fixture, kitchen, "cajero", TenantUserRole.Staff, permissions: [TenantPermission.ManageOrders], chooseOrderCancellations: true);
        await MemberAsync(fixture, kitchen, "mesero", TenantUserRole.Staff, permissions: [TenantPermission.ManageOrders]);
        var (order, cancelled) = await CancelledAtCustomerRequestAsync(fixture, raised, kitchen, tacos, cashier, "Llamó: ya no lo quiere");
        Assert.Equal((false, true), (cancelled.ByCustomer, cancelled.AtCustomerRequest));

        var queued = await QueueAsync(fixture, kitchen, order, cancelled, cancelledBy: cashier);
        Assert.Equal([(owner, true)], queued.Select(q => (q.Event.UserId, q.Event.AtCustomerRequest)));
        Assert.All(queued, q => Assert.Equal((order.Id, (Guid?)kitchen.Id), (q.Event.OrderId, q.TenantId)));
        // Had the owner cancelled it, the cashier would hear of it.
        Assert.Equal([cashier], (await QueueAsync(fixture, kitchen, order, cancelled, cancelledBy: owner)).Select(q => q.Event.UserId));
        // Cancelled by the business on its own: nobody needs to be told.
        Assert.Empty(await QueueAsync(fixture, kitchen, order, cancelled with { AtCustomerRequest = false }, cancelledBy: cashier));

        // Its customer cancelled theirs: whoever gets them hears they did, not that they asked.
        var byCustomer = await CancelledByCustomerAsync(fixture, kitchen, tacos);
        Assert.Equal(
            new[] { (owner, false), (cashier, false) }.Order(),
            (await QueueAsync(fixture, kitchen, byCustomer, byCustomer: true)).Select(q => (q.Event.UserId, q.Event.AtCustomerRequest)).Order());
        // The owner cancelled their own order from their account: they know too.
        var ownOrder = new OrderCancelledEvent(byCustomer.Id, kitchen.Id, OrderStatus.Pending, Order.CustomerCancellationReason, ByCustomer: true);
        Assert.Equal([cashier], (await QueueAsync(fixture, kitchen, byCustomer, ownOrder, cancelledBy: owner)).Select(q => q.Event.UserId));

        // A sale of the counter its customer asked to cancel: the emails are of the online orders.
        var (counter, ofTheCounter) = await CancelledAtCustomerRequestAsync(fixture, raised, kitchen, tacos, cashier, null, online: false);
        Assert.True(ofTheCounter.AtCustomerRequest);
        Assert.Empty(await QueueAsync(fixture, kitchen, counter, ofTheCounter, cancelledBy: cashier));

        // The business's activity says its customer asked, with the reason.
        await new OrderAuditHandlers(fixture.Context).HandleAsync(cancelled, Context(kitchen) with { ActorUserId = cashier.ToString(), ActorName = CashierName }, CancellationToken.None);
        await fixture.Context.SaveChangesAsync();
        var activity = await fixture.Context.AuditLogs.IgnoreQueryFilters().SingleAsync(a => a.Action == "OrderCancelled" && a.EntityId == order.Id.ToString());
        Assert.Contains("\"AtCustomerRequest\":true", activity.Metadata!);
        Assert.Contains("\"ByCustomer\":false", activity.Metadata!);
    }

    [Fact]
    public async Task TheEmail_ShouldSayItsCustomerAskedToCancelIt_AndWhy_NeverWhoCancelledIt()
    {
        var raised = new RaisedEvents();
        await using var fixture = SalesTestFixture.Create(raised);
        var (kitchen, tacos) = await KitchenAsync(fixture, "cocina", "Cocina <Luna>");
        var owner = await MemberAsync(fixture, kitchen, "dueña", TenantUserRole.Owner);
        var cashier = await MemberAsync(fixture, kitchen, "cajero", TenantUserRole.Staff, permissions: [TenantPermission.ManageOrders]);
        var (order, _) = await CancelledAtCustomerRequestAsync(fixture, raised, kitchen, tacos, cashier, "Llamó:\n<i>ya no</i> lo quiere", customer: "<b>Mariana</b>");

        var (to, subject, body) = Assert.Single(await SendAsync(fixture, kitchen, order, owner, atCustomerRequest: true));

        Assert.Equal($"dueña.{kitchen.Slug}@demo.com", to);
        Assert.Equal($"Pedido cancelado en Cocina <Luna>: {order.OrderNumber} por $120.00", subject);
        Assert.Contains("<h2>Un cliente pidió cancelar su pedido</h2>", body);
        Assert.Contains($"<strong>&lt;b&gt;Mariana&lt;/b&gt;</strong> pidió cancelar su pedido en línea <strong>{order.OrderNumber}</strong> de Cocina &lt;Luna&gt;, por un total de <strong>$120.00</strong>, y el equipo lo canceló.", body);
        Assert.Contains("<p>Motivo: Llam&#243;: &lt;i&gt;ya no&lt;/i&gt; lo quiere</p>", body);
        Assert.Contains("<p>Ya no tienes que prepararlo ni entregarlo.</p>", body);
        Assert.Contains($"<a href='{AppUrl}/orders'>Abrir Pedidos</a>", body);
        Assert.Contains("recibes los correos de los pedidos en línea de Cocina &lt;Luna&gt; que sus clientes cancelan o piden cancelar", body);
        Assert.DoesNotContain("<b>", body);
        Assert.DoesNotContain("<i>", body);
        Assert.DoesNotContain("antes de que lo aceptaran", body);
        Assert.DoesNotContain("1234", body);
        // Nor who cancelled it, as written or encoded.
        Assert.DoesNotContain(CashierName, body);
        Assert.DoesNotContain(WebUtility.HtmlEncode(CashierName), body);
        Assert.DoesNotContain("cajero.", body);

        // Without a reason, none is said.
        var (silent, _) = await CancelledAtCustomerRequestAsync(fixture, raised, kitchen, tacos, cashier, null);
        Assert.DoesNotContain("Motivo", Assert.Single(await SendAsync(fixture, kitchen, silent, owner, atCustomerRequest: true)).Body);
    }

    [Fact]
    public async Task EachEmail_ShouldGoOnlyAsItsOrderWasCancelled_ADayAgoOrLess()
    {
        var raised = new RaisedEvents();
        await using var fixture = SalesTestFixture.Create(raised);
        var (kitchen, tacos) = await KitchenAsync(fixture, "cocina", "Cocina Luna");
        var owner = await MemberAsync(fixture, kitchen, "dueña", TenantUserRole.Owner);
        var cashier = await MemberAsync(fixture, kitchen, "cajero", TenantUserRole.Staff, permissions: [TenantPermission.ManageOrders]);
        var (asked, _) = await CancelledAtCustomerRequestAsync(fixture, raised, kitchen, tacos, cashier, "Llamó");
        var byCustomer = await CancelledByCustomerAsync(fixture, kitchen, tacos);

        // The email of one its customer asked to cancel, for one its customer cancelled, and the other way around: none.
        Assert.Empty(await SendAsync(fixture, kitchen, byCustomer, owner, atCustomerRequest: true));
        Assert.Empty(await SendAsync(fixture, kitchen, asked, owner));
        Assert.Single(await SendAsync(fixture, kitchen, byCustomer, owner));

        // A day after the business cancelled it, to the minute: still sent. A minute later, whoever confirms orders saw it.
        Assert.Single(await SendAsync(fixture, kitchen, asked, owner, atCustomerRequest: true, after: TimeSpan.FromDays(1)));
        Assert.Empty(await SendAsync(fixture, kitchen, asked, owner, atCustomerRequest: true, after: TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task AnOrderOfAnotherBusiness_ShouldNeverBeTold_NotEvenToWhoGetsTheirOwn()
    {
        await using var fixture = SalesTestFixture.Create();
        var (kitchen, tacos) = await KitchenAsync(fixture, "cocina", "Cocina Luna");
        var owner = await MemberAsync(fixture, kitchen, "dueña", TenantUserRole.Owner);
        var (other, _) = await KitchenAsync(fixture, "otra", "Cocina Otra");
        var otherOwner = await MemberAsync(fixture, other, "otra", TenantUserRole.Owner);
        var order = await CancelledByCustomerAsync(fixture, kitchen, tacos);

        // The message of Cocina Luna's order read in Cocina Otra: neither its owner nor Cocina Luna's hear of it.
        Assert.Empty(await SendAsync(fixture, other, order, otherOwner));
        Assert.Empty(await SendAsync(fixture, other, order, owner));
        // In its own business, it is sent.
        Assert.Single(await SendAsync(fixture, kitchen, order, owner));
    }

    [Fact]
    public async Task AFailureOfTheProvider_ShouldThrow_ForTheOutboxToRetryThatEmailAlone()
    {
        await using var fixture = SalesTestFixture.Create();
        var (kitchen, tacos) = await KitchenAsync(fixture, "cocina", "Cocina Luna");
        var owner = await MemberAsync(fixture, kitchen, "dueña", TenantUserRole.Owner);
        var order = await CancelledByCustomerAsync(fixture, kitchen, tacos);

        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(fixture, kitchen, order, owner, failing: true));
    }

    [Fact]
    public async Task EachPerson_ShouldChooseTheirOwn_TheOwnersGettingThemUntilTheyChoose()
    {
        await using var fixture = SalesTestFixture.Create();
        var (kitchen, _) = await KitchenAsync(fixture, "cocina", "Cocina Luna");
        var owner = await MemberAsync(fixture, kitchen, "dueña", TenantUserRole.Owner);
        var cashier = await MemberAsync(fixture, kitchen, "cajero", TenantUserRole.Staff, permissions: [TenantPermission.ManageOrders], emailConfirmed: false);

        var ofTheOwner = await GetAsync(fixture, kitchen, owner);
        Assert.Equal((true, $"dueña.{kitchen.Slug}@demo.com", true), (ofTheOwner.Value!.Enabled, ofTheOwner.Value.Email, ofTheOwner.Value.EmailConfirmed));
        var ofTheCashier = await GetAsync(fixture, kitchen, cashier);
        Assert.Equal((false, false), (ofTheCashier.Value!.Enabled, ofTheCashier.Value.EmailConfirmed));

        var turnedOn = await UpdateAsync(fixture, kitchen, cashier, enabled: true);
        Assert.True(turnedOn.Value!.Enabled);
        var turnedOff = await UpdateAsync(fixture, kitchen, owner, enabled: false);
        Assert.False(turnedOff.Value!.Enabled);
        var stored = await fixture.Context.TenantUsers.IgnoreQueryFilters().AsNoTracking().Where(m => m.TenantId == kitchen.Id)
            .ToDictionaryAsync(m => m.UserId, m => m.OrderCancellationEmails);
        Assert.Equal((false, true), (stored[owner], stored[cashier]));

        // Someone who is not of the team.
        var stranger = await GetAsync(fixture, kitchen, Guid.NewGuid());
        Assert.Equal(NoticeEmailPreferences.NotAMemberMessage, stranger.Error);
    }

    [Fact]
    public void OnlyWhoeverMayConfirmTheOrders_InABusinessThatSells_ShouldGetThem_OrChooseThem()
    {
        // As the API asks to confirm orders: ManageOrders (or running the team), in a business with food, a shop or services.
        TenantAccessSnapshot Access(TenantCapability modules, bool suspended = false, bool manager = false, params string[] permissions)
            => new(true, modules, permissions, TenantSuspended: suspended, IsManager: manager);
        Assert.True(OrderCancellationEmailRecipients.MayConfirmOrders(Access(TenantCapability.Food, permissions: "ManageOrders")));
        Assert.True(OrderCancellationEmailRecipients.MayConfirmOrders(Access(TenantCapability.Retail, manager: true)));
        Assert.True(OrderCancellationEmailRecipients.MayConfirmOrders(Access(TenantCapability.Service, permissions: "ManageOrders")));
        Assert.False(OrderCancellationEmailRecipients.MayConfirmOrders(Access(TenantCapability.Food, permissions: "ManageCatalog")));
        Assert.False(OrderCancellationEmailRecipients.MayConfirmOrders(Access(TenantCapability.Appointments, manager: true, permissions: "ManageOrders")));
        Assert.False(OrderCancellationEmailRecipients.MayConfirmOrders(Access(TenantCapability.Food, suspended: true, permissions: "ManageOrders")));

        foreach (var request in new[] { typeof(GetOrderCancellationEmailsQuery), typeof(UpdateOrderCancellationEmailsCommand) })
        {
            var permission = Assert.Single(request.GetCustomAttributes(typeof(RequiresPermissionAttribute), false).Cast<RequiresPermissionAttribute>());
            var capability = Assert.Single(request.GetCustomAttributes(typeof(RequiresCapabilityAttribute), false).Cast<RequiresCapabilityAttribute>());
            Assert.Equal([TenantPermission.ManageOrders], permission.AnyOf);
            Assert.Equal(TenantCapability.Food | TenantCapability.Retail | TenantCapability.Service, capability.AnyOf);
        }
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
        // The fixture has no auditing interceptor: the order was placed now, as the database would stamp it.
        await ChangeAsync(fixture, order.Id, o => o.CreatedAt = fixture.Clock.UtcNow);
        return order;
    }

    /// <summary>An order its customer cancelled from the link of its receipt, as the API does.</summary>
    private static async Task<Order> CancelledByCustomerAsync(SalesTestFixture fixture, Tenant tenant, Product product, string customer = "Mariana Ortiz")
    {
        var order = await PlaceAsync(fixture, product, tenant.Slug, customer);
        var cancelled = await new CancelPublicOrderCommandHandler(fixture.Context, fixture.TenantService, NoOpOrderInventoryService.Instance, fixture.Clock)
            .Handle(new CancelPublicOrderCommand(order.TrackingToken!), CancellationToken.None);
        Assert.True(cancelled.IsSuccess, cancelled.Error);
        fixture.Context.ChangeTracker.Clear();
        return order;
    }

    private const string CashierName = "Cajero Ramírez";

    /// <summary>
    /// An order of the online menu (or a sale of the counter) the business accepted, and <paramref name="cancelledBy"/> cancelled
    /// as the API does, saying its customer asked; and the event it raised.
    /// </summary>
    private static async Task<(Order Order, OrderCancelledEvent Raised)> CancelledAtCustomerRequestAsync(
        SalesTestFixture fixture, RaisedEvents raised, Tenant tenant, Product product, Guid cancelledBy, string? reason,
        string customer = "Mariana Ortiz", bool online = true)
    {
        Order order;
        if (online)
        {
            order = await PlaceAsync(fixture, product, tenant.Slug, customer);
            await ChangeAsync(fixture, order.Id, o => o.TransitionTo(OrderStatus.Confirmed, OrderStateMachineFactory.Create(o.BusinessType, o.OrderType), "dueña"));
        }
        else
        {
            fixture.TenantService.SetTenant(tenant.Id);
            var sold = await fixture.CreateOrderHandler().Handle(new CreateOrderCommand
            {
                CustomerName = customer,
                CustomerPhone = "55 1234 5678",
                Items = [new CreateOrderItemDto { ProductId = product.Id, Quantity = 1 }]
            }, CancellationToken.None);
            Assert.True(sold.IsSuccess, sold.Error);
            fixture.Context.ChangeTracker.Clear();
            order = await fixture.Context.Orders.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Id == sold.Value.Id);
            Assert.Equal(OrderChannel.Counter, order.Channel);
        }

        fixture.TenantService.SetTenant(tenant.Id);
        fixture.CurrentUser.UserId = cancelledBy.ToString();
        fixture.CurrentUser.UserName = CashierName;
        var cancelled = await new CancelOrderCommandHandler(fixture.Context, fixture.CurrentUser, NoOpOrderInventoryService.Instance)
            .Handle(new CancelOrderCommand { OrderId = order.Id, Reason = reason, AtCustomerRequest = true }, CancellationToken.None);
        Assert.True(cancelled.IsSuccess, cancelled.Error);
        fixture.Context.ChangeTracker.Clear();
        return (order, raised.OfType<OrderCancelledEvent>().Last(e => e.OrderId == order.Id));
    }

    private static async Task ChangeAsync(SalesTestFixture fixture, Guid orderId, Action<Order> change)
    {
        var order = await fixture.Context.Orders.IgnoreQueryFilters().Include(o => o.StatusHistory).SingleAsync(o => o.Id == orderId);
        change(order);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    private static Task<List<(OrderCancellationEmailEvent Event, Guid? TenantId)>> QueueAsync(
        SalesTestFixture fixture, Tenant tenant, Order order, bool byCustomer)
        => QueueAsync(fixture, tenant, order, new OrderCancelledEvent(order.Id, tenant.Id, OrderStatus.Pending, null, byCustomer), cancelledBy: null);

    /// <summary>What the outbox queues for <paramref name="cancelled"/>, raised by <paramref name="cancelledBy"/>.</summary>
    private static async Task<List<(OrderCancellationEmailEvent Event, Guid? TenantId)>> QueueAsync(
        SalesTestFixture fixture, Tenant tenant, Order order, OrderCancelledEvent cancelled, Guid? cancelledBy)
    {
        Assert.Equal(order.Id, cancelled.OrderId);
        fixture.TenantService.SetTenant(tenant.Id);
        var outbox = new Outbox<OrderCancellationEmailEvent>();
        await new OrderCancelledEmailsHandler(fixture.Context, new MembersAccess(fixture.Context), outbox)
            .HandleAsync(cancelled, Context(tenant) with { ActorUserId = cancelledBy?.ToString() }, CancellationToken.None);
        return outbox.Added;
    }

    /// <summary>
    /// Sends one member's email, <paramref name="after"/> the customer cancelled it (a minute by default), with an email
    /// provider that fails when <paramref name="failing"/>.
    /// </summary>
    private static async Task<List<(string To, string Subject, string Body)>> SendAsync(
        SalesTestFixture fixture, Tenant tenant, Order order, Guid userId, TimeSpan? after = null, bool failing = false,
        bool atCustomerRequest = false)
    {
        var cancelledAt = await fixture.Context.OrderStatusHistories.IgnoreQueryFilters().AsNoTracking()
            .Where(h => h.OrderId == order.Id && h.NewStatus == OrderStatus.Cancelled)
            .Select(h => h.ChangedAt)
            .SingleAsync();
        fixture.Clock.UtcNow = cancelledAt + (after ?? TimeSpan.FromMinutes(1));
        fixture.TenantService.SetTenant(tenant.Id);
        var emails = new Emails(fail: failing);
        await new OrderCancellationEmailHandler(fixture.Context, new MembersAccess(fixture.Context), emails, new Links(AppUrl), fixture.Clock)
            .HandleAsync(new OrderCancellationEmailEvent(order.Id, userId, atCustomerRequest), Context(tenant), CancellationToken.None);
        return emails.Sent;
    }

    private static async Task<Result<OrderCancellationEmailsDto>> GetAsync(SalesTestFixture fixture, Tenant tenant, Guid userId)
    {
        SignIn(fixture, tenant, userId);
        return await new OrderCancellationEmailsHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser)
            .Handle(new GetOrderCancellationEmailsQuery(), CancellationToken.None);
    }

    private static async Task<Result<OrderCancellationEmailsDto>> UpdateAsync(SalesTestFixture fixture, Tenant tenant, Guid userId, bool enabled)
    {
        SignIn(fixture, tenant, userId);
        var result = await new OrderCancellationEmailsHandler(fixture.Context, fixture.TenantService, fixture.CurrentUser)
            .Handle(new UpdateOrderCancellationEmailsCommand(enabled), CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        return result;
    }

    private static void SignIn(SalesTestFixture fixture, Tenant tenant, Guid userId)
    {
        fixture.TenantService.SetTenant(tenant.Id);
        fixture.CurrentUser.UserId = userId.ToString();
    }

    /// <summary>What the context publishes once its changes are saved: the events the orders raised.</summary>
    private sealed class RaisedEvents : List<object>, IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Add(notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Publish((object)notification, cancellationToken);

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
