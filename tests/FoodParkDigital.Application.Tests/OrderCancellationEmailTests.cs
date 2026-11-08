using FoodParkDigital.Application.Common.Interfaces;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Application.Common.Security;
using FoodParkDigital.Application.Common.Notices;
using FoodParkDigital.Application.Customers.Services;
using FoodParkDigital.Application.OnlineMenu;
using FoodParkDigital.Application.Orders;
using FoodParkDigital.Application.Orders.CancellationNotices;
using FoodParkDigital.Application.Orders.Commands;
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
/// The email of an online order its customer cancelled before the business accepted it (Fase 30B, ADR 0082): one per member who
/// gets them and may confirm orders, what it says and never says, when it is no longer sent, and each person's own choice.
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

    private static async Task ChangeAsync(SalesTestFixture fixture, Guid orderId, Action<Order> change)
    {
        var order = await fixture.Context.Orders.IgnoreQueryFilters().Include(o => o.StatusHistory).SingleAsync(o => o.Id == orderId);
        change(order);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
    }

    private static async Task<List<(OrderCancellationEmailEvent Event, Guid? TenantId)>> QueueAsync(
        SalesTestFixture fixture, Tenant tenant, Order order, bool byCustomer)
    {
        fixture.TenantService.SetTenant(tenant.Id);
        var outbox = new Outbox<OrderCancellationEmailEvent>();
        await new OrderCancelledEmailsHandler(fixture.Context, new MembersAccess(fixture.Context), outbox)
            .HandleAsync(new OrderCancelledEvent(order.Id, tenant.Id, OrderStatus.Pending, null, byCustomer), Context(tenant), CancellationToken.None);
        return outbox.Added;
    }

    /// <summary>
    /// Sends one member's email, <paramref name="after"/> the customer cancelled it (a minute by default), with an email
    /// provider that fails when <paramref name="failing"/>.
    /// </summary>
    private static async Task<List<(string To, string Subject, string Body)>> SendAsync(
        SalesTestFixture fixture, Tenant tenant, Order order, Guid userId, TimeSpan? after = null, bool failing = false)
    {
        var cancelledAt = await fixture.Context.OrderStatusHistories.IgnoreQueryFilters().AsNoTracking()
            .Where(h => h.OrderId == order.Id && h.NewStatus == OrderStatus.Cancelled)
            .Select(h => h.ChangedAt)
            .SingleAsync();
        fixture.Clock.UtcNow = cancelledAt + (after ?? TimeSpan.FromMinutes(1));
        fixture.TenantService.SetTenant(tenant.Id);
        var emails = new Emails(fail: failing);
        await new OrderCancellationEmailHandler(fixture.Context, new MembersAccess(fixture.Context), emails, new Links(AppUrl), fixture.Clock)
            .HandleAsync(new OrderCancellationEmailEvent(order.Id, userId), Context(tenant), CancellationToken.None);
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
}
