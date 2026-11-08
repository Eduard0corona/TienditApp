using FoodParkDigital.Application.Attention;
using FoodParkDigital.Application.Tests.Support;
using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Finance.Entities;
using FoodParkDigital.Domain.Inventory.Entities;
using FoodParkDigital.Domain.Messaging.Entities;
using FoodParkDigital.Domain.Messaging.Enums;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoodParkDigital.Application.Tests;

/// <summary>The bell and "Requiere atención" count what needs someone now, only for what the person may handle.</summary>
public class AttentionQueryTests
{
    private const TenantCapability Restaurant = TenantCapability.Food | TenantCapability.Inventory | TenantCapability.Kitchen;

    [Fact]
    public async Task Owner_ShouldSeeWhatWaitsForTheBusiness()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, Restaurant);
        AddOrder(fixture, tenant, "ORD-1", OrderChannel.Online);
        AddOrder(fixture, tenant, "ORD-2", OrderChannel.Online);
        AddOrder(fixture, tenant, "ORD-3", OrderChannel.Counter);
        await AddStockAsync(fixture, tenant, "Leche", onHand: 0m, reorderPoint: 5m);
        await AddStockAsync(fixture, tenant, "Café", onHand: 2m, reorderPoint: 4m);
        await AddStockAsync(fixture, tenant, "Azúcar", onHand: 30m, reorderPoint: 4m);
        AddDrawer(fixture, tenant, openedAt: SalesTestFixture.Now.AddDays(-1), openedBy: Guid.NewGuid());
        AddDrawer(fixture, tenant, openedAt: SalesTestFixture.Now.AddHours(-1), openedBy: Guid.NewGuid());
        AddFailedMessage(fixture, tenant, failedAt: SalesTestFixture.Now.AddHours(-2));
        AddFailedMessage(fixture, tenant, failedAt: SalesTestFixture.Now.AddDays(-3));
        await fixture.Context.SaveChangesAsync();

        var attention = await HandleAsync(fixture);

        Assert.Equal(2, Count(attention, AttentionKeys.OnlineOrdersToConfirm));
        var lowStock = Assert.Single(attention.Items, i => i.Key == AttentionKeys.LowStock);
        Assert.Equal(2, lowStock.Count);
        Assert.Equal(1, lowStock.UrgentCount);
        Assert.Equal(1, Count(attention, AttentionKeys.DrawersOpenSinceYesterday));
        Assert.Equal(1, Count(attention, AttentionKeys.FailedMessages));
    }

    [Fact]
    public async Task AnOrderOfTheOnlineMenu_WaitingFifteenMinutes_ShouldBeUrgent_NotOneOfATable_OrOfTheTeam()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, Restaurant);
        // Waiting 15 and 40 minutes: urgent (Fase 29X). Fourteen minutes, of a table or taken by the team online: not yet, or
        // never; still to confirm, though.
        AddOrder(fixture, tenant, "ORD-1", OrderChannel.Online, arrivedMinutesAgo: 15);
        AddOrder(fixture, tenant, "ORD-2", OrderChannel.Online, arrivedMinutesAgo: 40);
        AddOrder(fixture, tenant, "ORD-3", OrderChannel.Online, arrivedMinutesAgo: 14);
        AddOrder(fixture, tenant, "ORD-4", OrderChannel.Online, arrivedMinutesAgo: 40, table: "Mesa 4");
        AddOrder(fixture, tenant, "ORD-5", OrderChannel.Online, arrivedMinutesAgo: 40, byTeam: true);
        // Answered already: not to confirm.
        AddOrder(fixture, tenant, "ORD-6", OrderChannel.Online, arrivedMinutesAgo: 40)
            .TransitionTo(OrderStatus.Confirmed, new FoodOrderStateMachine(), "cajero@demo.com");
        await fixture.Context.SaveChangesAsync();

        var toConfirm = Assert.Single((await HandleAsync(fixture)).Items, i => i.Key == AttentionKeys.OnlineOrdersToConfirm);

        Assert.Equal((5, 2), (toConfirm.Count, toConfirm.UrgentCount));
    }

    [Fact]
    public async Task Cashier_ShouldOnlySeeTheirOwnDrawerLeftOpen()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, Restaurant);
        var cashier = Guid.NewGuid();
        fixture.CurrentUser.UserId = cashier.ToString();
        fixture.CurrentUser.Role = "STAFF";
        fixture.CurrentUser.TenantRole = "Staff";
        fixture.CurrentUser.Permissions = [nameof(TenantPermission.ManageCash)];
        AddOrder(fixture, tenant, "ORD-1", OrderChannel.Online);
        await AddStockAsync(fixture, tenant, "Leche", onHand: 0m, reorderPoint: 5m);
        AddDrawer(fixture, tenant, openedAt: SalesTestFixture.Now.AddDays(-1), openedBy: cashier);
        AddDrawer(fixture, tenant, openedAt: SalesTestFixture.Now.AddDays(-1), openedBy: Guid.NewGuid());
        await fixture.Context.SaveChangesAsync();

        var attention = await HandleAsync(fixture);

        // Taking orders and the inventory are someone else's job; of the drawers, only theirs is.
        var item = Assert.Single(attention.Items);
        Assert.Equal(AttentionKeys.DrawersOpenSinceYesterday, item.Key);
        Assert.Equal(1, item.Count);
    }

    [Fact]
    public async Task NothingPending_ShouldBeAnEmptyList()
    {
        await using var fixture = SalesTestFixture.Create();
        var tenant = await fixture.AddTenantAsync(BusinessType.Food, Restaurant);
        AddDrawer(fixture, tenant, openedAt: SalesTestFixture.Now.AddHours(-3), openedBy: Guid.NewGuid());
        await fixture.Context.SaveChangesAsync();

        var attention = await HandleAsync(fixture);

        Assert.Empty(attention.Items);
    }

    private static Task<AttentionDto> HandleAsync(SalesTestFixture fixture)
        => new GetAttentionQueryHandler(
                fixture.Context,
                TestAccess.For(fixture.CurrentUser, fixture.TenantService),
                fixture.CurrentUser,
                fixture.Clock)
            .Handle(new GetAttentionQuery(), CancellationToken.None);

    private static int Count(AttentionDto attention, string key) => Assert.Single(attention.Items, i => i.Key == key).Count;

    private static Order AddOrder(
        SalesTestFixture fixture, Tenant tenant, string number, OrderChannel channel, int arrivedMinutesAgo = 1,
        string? table = null, bool byTeam = false)
    {
        // A customer's own order belongs to a (temporary) customer; a counter sale is the business's.
        var byCustomer = channel == OrderChannel.Online && !byTeam;
        var order = new Order(
            tenant.Id, null, byCustomer ? Guid.NewGuid() : null, createdByBusiness: !byCustomer, number, BusinessType.Food,
            OrderType.Retail, "Ana", "+525512345678", null, table, channel: channel)
        {
            CreatedAt = SalesTestFixture.Now.AddMinutes(-arrivedMinutesAgo)
        };
        fixture.Context.Orders.Add(order);
        return order;
    }

    private static async Task AddStockAsync(SalesTestFixture fixture, Tenant tenant, string name, decimal onHand, decimal reorderPoint)
    {
        var supply = new SupplyItem(tenant.Id, name, UnitType.Liter, reorderPoint: reorderPoint);
        var location = await fixture.Context.InventoryLocations.SingleAsync(l => l.TenantId == tenant.Id);
        var stock = new InventoryStock(tenant.Id, supply.Id, location.Id);
        if (onHand > 0)
            stock.ApplyMovement(onHand, 10m, SalesTestFixture.Now.AddDays(-5));
        fixture.Context.SupplyItems.Add(supply);
        fixture.Context.InventoryStocks.Add(stock);
    }

    private static void AddDrawer(SalesTestFixture fixture, Tenant tenant, DateTimeOffset openedAt, Guid openedBy)
        => fixture.Context.CashSessions.Add(new CashSession(tenant.Id, openedAt, "cajero", 500m, openedByUserId: openedBy));

    private static void AddFailedMessage(SalesTestFixture fixture, Tenant tenant, DateTimeOffset failedAt)
    {
        var message = new OutgoingMessage(tenant.Id, MessagePurpose.Campaign, "+525512345678", "Ana", "Promoción", null, "es_MX", [], failedAt);
        message.Fail("Número sin WhatsApp");
        message.CreatedAt = failedAt;
        message.UpdatedAt = failedAt;
        fixture.Context.OutgoingMessages.Add(message);
    }
}
