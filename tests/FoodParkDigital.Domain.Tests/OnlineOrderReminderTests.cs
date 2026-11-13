using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Entities;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// Reminding a business by email of an order of its online menu still waiting to be accepted (Fase 31K, ADR 0091): once,
/// from the 15 minutes the bell marks it until the half hour it counts among the slowest; never one taken by the team, of a
/// table or already answered.
/// </summary>
public class OnlineOrderReminderTests
{
    private static readonly Guid Business = Guid.NewGuid();
    private static readonly DateTimeOffset Arrived = new(2026, 11, 12, 19, 0, 0, TimeSpan.Zero);
    private static readonly FoodOrderStateMachine Food = new();

    [Fact]
    public void RemindAt_ShouldBeWhenTheBellMarksIt_HalfwayToCountingAmongTheSlowest()
    {
        Assert.Equal(Arrived.AddMinutes(15), OrderConfirmTimes.RemindAt(Arrived));
        Assert.Equal(Arrived.AddMinutes(30), OrderConfirmTimes.ConfirmBy(Arrived));
    }

    [Fact]
    public void AnOrderStillWaiting_ShouldBeRemindedOnce_FromFifteenMinutesUntilTheHalfHour()
    {
        var order = OnlineOrder();

        Assert.True(order.WaitsForConfirmation);
        Assert.False(order.RemindToConfirm(Arrived.AddMinutes(15).AddTicks(-1)));
        Assert.Null(order.ConfirmReminderSentAt);
        Assert.True(order.RemindToConfirm(Arrived.AddMinutes(15)));
        Assert.Equal(Arrived.AddMinutes(15), order.ConfirmReminderSentAt);
        var reminder = Assert.IsType<OnlineOrderWaitingEvent>(Assert.Single(order.DomainEvents, e => e is OnlineOrderWaitingEvent));
        Assert.Equal((order.Id, Business), (reminder.OrderId, reminder.TenantId));

        // Once: a later pass changes nothing.
        Assert.False(order.RemindToConfirm(Arrived.AddMinutes(20)));
        Assert.Equal(Arrived.AddMinutes(15), order.ConfirmReminderSentAt);
        Assert.Single(order.DomainEvents, e => e is OnlineOrderWaitingEvent);
    }

    [Fact]
    public void AnOrderThatReachedTheHalfHour_ShouldNotBeReminded_TheBellKeepsMarkingIt()
    {
        var order = OnlineOrder();

        Assert.True(OnlineOrder().RemindToConfirm(Arrived.AddMinutes(30).AddTicks(-1)));
        Assert.False(order.RemindToConfirm(Arrived.AddMinutes(30)));
        Assert.False(order.RemindToConfirm(Arrived.AddHours(5)));
        Assert.Null(order.ConfirmReminderSentAt);
        Assert.DoesNotContain(order.DomainEvents, e => e is OnlineOrderWaitingEvent);
    }

    [Fact]
    public void AnOrderAlreadyAnsweredOrCancelled_ShouldNotBeReminded()
    {
        var accepted = OnlineOrder();
        accepted.TransitionTo(OrderStatus.Confirmed, Food, "Caja");
        var cancelled = OnlineOrder();
        cancelled.CancelByCustomer(Food);

        foreach (var order in new[] { accepted, cancelled })
        {
            Assert.False(order.WaitsForConfirmation);
            Assert.False(order.RemindToConfirm(Arrived.AddMinutes(16)));
            Assert.Null(order.ConfirmReminderSentAt);
        }
    }

    [Fact]
    public void AnOrderTakenByTheTeam_OfATable_OrOfAnotherChannel_ShouldNotBeReminded()
    {
        var takenByTheTeam = OnlineOrder(createdByBusiness: true);
        var ofATable = OnlineOrder(tableNumber: "4");
        var openTab = OnlineOrder();
        openTab.OpenAsTab(Guid.NewGuid(), "Mesa 2", Arrived, "Mesero");
        var counter = OnlineOrder(channel: OrderChannel.Counter);

        foreach (var order in new[] { takenByTheTeam, ofATable, openTab, counter })
        {
            Assert.False(order.WaitsForConfirmation);
            Assert.False(order.RemindToConfirm(Arrived.AddMinutes(16)));
            Assert.Null(order.ConfirmReminderSentAt);
            Assert.DoesNotContain(order.DomainEvents, e => e is OnlineOrderWaitingEvent);
        }
    }

    [Fact]
    public void ReviewEmails_ShouldReachOnlyTheOwners_UntilEachChooses()
    {
        var owner = new TenantUser(Business, Guid.NewGuid(), TenantUserRole.Owner);
        var admin = new TenantUser(Business, Guid.NewGuid(), TenantUserRole.Admin);

        Assert.Null(owner.ReviewEmails);
        Assert.True(owner.GetsReviewEmails);
        Assert.False(admin.GetsReviewEmails);

        owner.ChooseReviewEmails(false);
        admin.ChooseReviewEmails(true);
        Assert.Equal((false, false), (owner.ReviewEmails, owner.GetsReviewEmails));
        Assert.Equal((true, true), (admin.ReviewEmails, admin.GetsReviewEmails));
    }

    private static Order OnlineOrder(
        OrderChannel channel = OrderChannel.Online, string? tableNumber = null, bool createdByBusiness = false)
    {
        var order = new Order(
            tenantId: Business,
            customerUserId: null,
            temporaryCustomerId: Guid.NewGuid(),
            createdByBusiness: createdByBusiness,
            orderNumber: "ORD-000731",
            businessType: BusinessType.Food,
            orderType: OrderType.Retail,
            customerName: "Valeria Morales",
            customerPhone: "+525522107765",
            customerNotes: null,
            tableNumber: tableNumber,
            pricesIncludeTax: true,
            channel: channel)
        {
            CreatedAt = Arrived
        };
        order.AddItem(Guid.NewGuid(), "Torta de jamón", 2, 117.75m);
        return order;
    }
}
