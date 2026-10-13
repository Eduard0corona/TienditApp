using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.Platforms;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// An order of Rappi, Uber Eats or DiDi Food: registered by the business, without customer, phone, table, tab or tip;
/// its folio, terms and amounts copied once into its detail; and, when the platform charged, paid then with a
/// «Plataforma» payment that only refunds as «Plataforma».
/// </summary>
public class PlatformOrderTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 18, 30, 0, TimeSpan.Zero);
    private static readonly Guid Location = Guid.NewGuid();

    [Theory]
    [InlineData(OrderChannel.Rappi)]
    [InlineData(OrderChannel.UberEats)]
    [InlineData(OrderChannel.DidiFood)]
    public void Constructor_ShouldTakeAnOrderOfTheBusiness_AndStoreBlankPhoneAndTableAsNone(OrderChannel channel)
    {
        var order = new Order(
            Tenant, null, null, createdByBusiness: true, Order.NumberNotAssigned, BusinessType.Food, OrderType.Retail,
            "Juan", customerPhone: "   ", customerNotes: "Sin cebolla", tableNumber: "", channel: channel);

        Assert.Equal(channel, order.Channel);
        Assert.True(order.IsDeliveryPlatformOrder);
        Assert.Null(order.CustomerPhone);
        Assert.Null(order.TableNumber);
        Assert.Null(order.CustomerUserId);
        Assert.Null(order.TemporaryCustomerId);
        Assert.Equal("Sin cebolla", order.CustomerNotes);
        Assert.Null(order.PlatformDetail);
    }

    [Theory]
    [InlineData(OrderChannel.Counter)]
    [InlineData(OrderChannel.WhatsApp)]
    [InlineData(OrderChannel.Online)]
    public void Constructor_ShouldKeepTheOtherChannelsAsTheyWere(OrderChannel channel)
    {
        var order = new Order(
            Tenant, null, Guid.NewGuid(), createdByBusiness: false, Order.NumberNotAssigned, BusinessType.Food, OrderType.Retail,
            "Ana", customerPhone: "5512345678", customerNotes: null, tableNumber: "Mesa 4", channel: channel);

        Assert.False(order.IsDeliveryPlatformOrder);
        Assert.Equal("5512345678", order.CustomerPhone);
        Assert.Equal("Mesa 4", order.TableNumber);
    }

    [Fact]
    public void Constructor_ShouldRefuseAnOrderOfAPlatformThatBreaksItsRules()
    {
        AssertRefused(PlatformOrderRules.RegisteredByBusinessMessage,
            () => NewOrder(createdByBusiness: false, temporaryCustomerId: Guid.NewGuid()));
        AssertRefused(PlatformOrderRules.RegisteredByBusinessMessage, () => NewOrder(createdByBusiness: false));
        AssertRefused(PlatformOrderRules.NoCustomerMessage, () => NewOrder(customerUserId: Guid.NewGuid()));
        AssertRefused(PlatformOrderRules.NoCustomerMessage, () => NewOrder(temporaryCustomerId: Guid.NewGuid()));
        AssertRefused(PlatformOrderRules.NoPhoneMessage, () => NewOrder(customerPhone: "5512345678"));
        AssertRefused(PlatformOrderRules.NoTableMessage, () => NewOrder(tableNumber: "Mesa 4"));
        AssertRefused(PlatformOrderRules.OnlyProductsMessage, () => NewOrder(orderType: OrderType.Service));
    }

    [Fact]
    public void RegisterPlatformOrder_WhenThePlatformCharged_ShouldPayTheTotalOutsideTheDrawers_AndCopyTheTerms()
    {
        // DiDi's example: $1,000 with IVA included, 30 % on the total, ISR 2.5 % and half the IVA withheld.
        var order = NewOrderWithItems();
        var setting = Setting();

        var detail = order.RegisterPlatformOrder(setting, PlatformOrderNumber.From(" #bc 953 "), PlatformCollectedBy.Platform, Now, Location);

        Assert.Same(detail, order.PlatformDetail);
        Assert.Equal(Tenant, detail.TenantId);
        Assert.Equal(order.Id, detail.OrderId);
        Assert.Equal(OrderChannel.Rappi, detail.Channel);
        Assert.Equal("BC953", detail.ExternalOrderNumber);
        Assert.Equal(PlatformCollectedBy.Platform, detail.CollectedBy);
        Assert.Equal(1, detail.SettingsRevision);
        Assert.Equal(
            (30m, PlatformCommissionBase.TotalWithTax, 16m, 2.5m, 50m),
            (detail.CommissionPercent, detail.CommissionBase, detail.CommissionVatPercent, detail.IsrWithholdingPercent, detail.VatWithholdingPercent));
        Assert.Equal(1000m, detail.OrderTotal);
        Assert.Equal(
            (300m, 48m, 21.55m, 68.97m, 561.48m),
            (detail.Commission, detail.CommissionTax, detail.IsrWithheld, detail.VatWithheld, detail.ExpectedSettlement));
        Assert.Equal(Now, detail.RegisteredAt);

        var payment = Assert.Single(order.Payments);
        Assert.Equal(OrderPaymentMethod.Platform, payment.Method);
        Assert.Equal(order.Total, payment.Amount);
        Assert.Equal(detail.RegisteredAt, payment.PaidAt);
        Assert.Equal("BC953", payment.Reference);
        Assert.Equal(Location, payment.InventoryLocationId);
        Assert.Equal(0m, payment.Tip);
        Assert.Null(payment.CashReceived);
        Assert.Equal(Tenant, payment.TenantId);
        Assert.Equal(order.Total, order.PaidAmount);
        Assert.Equal(0m, order.Balance);
    }

    [Fact]
    public void RegisterPlatformOrder_WhenTheBusinessCharges_ShouldLeaveItToBeCharged_WithoutWithholdings()
    {
        var order = NewOrderWithItems(channel: OrderChannel.DidiFood);

        var detail = order.RegisterPlatformOrder(
            Setting(OrderChannel.DidiFood), PlatformOrderNumber.From("12"), PlatformCollectedBy.Business, Now, Location);

        Assert.Empty(order.Payments);
        Assert.Equal(0m, order.PaidAmount);
        Assert.Equal(order.Total, order.Balance);
        Assert.Equal(PlatformCollectedBy.Business, detail.CollectedBy);
        Assert.Equal(
            (300m, 48m, 0m, 0m, -348m),
            (detail.Commission, detail.CommissionTax, detail.IsrWithheld, detail.VatWithheld, detail.ExpectedSettlement));

        // The courier pays on pickup: charged like any other order, through a drawer.
        order.RegisterPayment(order.Total, OrderPaymentMethod.Cash, Now, Location, null, null, null, cashReceived: 1000m);
        Assert.Equal(0m, order.Balance);
    }

    [Fact]
    public void RegisterPlatformOrder_ShouldCopyTheRevisionOfTheSettings()
    {
        var setting = Setting();
        setting.Update(isActive: true, PlatformTerms.WithFiscalDefaults(25m, PlatformCommissionBase.SubTotalWithoutTax));
        var order = NewOrderWithItems();

        var detail = order.RegisterPlatformOrder(setting, PlatformOrderNumber.From("152390"), PlatformCollectedBy.Platform, Now, null);

        Assert.Equal(2, detail.SettingsRevision);
        Assert.Equal(25m, detail.CommissionPercent);
        Assert.Equal(PlatformCommissionBase.SubTotalWithoutTax, detail.CommissionBase);
        // 25 % of 862.07 = 215.5175 -> 215.52.
        Assert.Equal(215.52m, detail.Commission);
        Assert.Null(Assert.Single(order.Payments).InventoryLocationId);
    }

    [Fact]
    public void RegisterPlatformOrder_ShouldRefuseASettingThatIsInactive_OfAnotherBusiness_OrOfAnotherPlatform()
    {
        AssertRefused("Rappi no está activa en tu negocio.", () => Register(NewOrderWithItems(), Setting(isActive: false)));
        AssertRefused("Uber Eats no está activa en tu negocio.",
            () => Register(NewOrderWithItems(channel: OrderChannel.UberEats), Setting(OrderChannel.UberEats, isActive: false)));
        AssertRefused(PlatformOrderRules.SettingOfAnotherPlatformMessage,
            () => Register(NewOrderWithItems(), Setting(tenantId: Guid.NewGuid())));
        AssertRefused(PlatformOrderRules.SettingOfAnotherPlatformMessage,
            () => Register(NewOrderWithItems(), Setting(OrderChannel.UberEats)));
    }

    [Fact]
    public void RegisterPlatformOrder_ShouldRefuseAnOrderThatIsNotANewOrderOfAPlatformWithProducts()
    {
        var counter = new Order(
            Tenant, null, null, true, Order.NumberNotAssigned, BusinessType.Food, OrderType.Retail, "Público en general",
            null, null, null, pricesIncludeTax: true);
        counter.AddItem(Guid.NewGuid(), "Tacos", 1, 100m);
        AssertRefused(PlatformOrderRules.OnlyPlatformChannelsMessage, () => Register(counter, Setting()));

        var registered = NewOrderWithItems();
        Register(registered, Setting());
        AssertRefused(PlatformOrderRules.AlreadyRegisteredMessage, () => Register(registered, Setting()));
        Assert.Single(registered.Payments);

        AssertRefused(PlatformOrderRules.NeedsAProductMessage, () => Register(NewOrder(), Setting()));
        var free = NewOrder();
        free.AddItem(Guid.NewGuid(), "Cubiertos", 1, 0m);
        AssertRefused(PlatformOrderRules.NeedsAProductMessage, () => Register(free, Setting()));

        var paid = NewOrderWithItems();
        paid.RegisterPayment(100m, OrderPaymentMethod.Cash, Now, null, null, null, null);
        AssertRefused(PlatformOrderRules.BeforeAnyPaymentMessage, () => Register(paid, Setting()));

        var cancelled = NewOrderWithItems();
        cancelled.Cancel(new FoodOrderStateMachine(), "cajero", "Lo registré dos veces");
        AssertRefused(PlatformOrderRules.CancelledMessage, () => Register(cancelled, Setting()));
        Assert.Null(cancelled.PlatformDetail);
    }

    [Fact]
    public void RegisterPlatformOrder_ShouldRefuseAnUnknownCollector()
    {
        var order = NewOrderWithItems();

        AssertRefused(PlatformOrderCalculator.InvalidCollectedByMessage, () => Register(order, Setting(), (PlatformCollectedBy)7));
        Assert.Null(order.PlatformDetail);
        Assert.Empty(order.Payments);
    }

    [Fact]
    public void AddItem_ShouldBeRefused_OnceTheCommissionWasCalculated()
    {
        var order = NewOrderWithItems();
        order.AddItem(Guid.NewGuid(), "Refresco", 1, 30m);
        Register(order, Setting(), PlatformCollectedBy.Business);

        AssertRefused(PlatformOrderRules.NoItemsAfterRegisteredMessage, () => order.AddItem(Guid.NewGuid(), "Postre", 1, 50m));
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(1030m, order.Total);
        Assert.Equal(1030m, order.PlatformDetail!.OrderTotal);
    }

    [Fact]
    public void OpenAsTab_ShouldBeRefused_ForAnOrderOfAPlatform()
    {
        var order = NewOrderWithItems();

        AssertRefused(PlatformOrderRules.NoTabMessage, () => order.OpenAsTab(null, null, Now, "mesero"));
        Assert.False(order.IsTab);
    }

    [Fact]
    public void RegisterPayment_ShouldNeverTakeThePlatformMethod_NorAnythingElseOnceThePlatformCharged()
    {
        var counter = new Order(
            Tenant, null, null, true, Order.NumberNotAssigned, BusinessType.Food, OrderType.Retail, "Público en general",
            null, null, null);
        counter.AddItem(Guid.NewGuid(), "Tacos", 1, 100m);
        AssertRefused(OrderPaymentMethodRules.PlatformOnlyOnCreationMessage,
            () => counter.RegisterPayment(counter.Total, OrderPaymentMethod.Platform, Now, null, null, null, null));

        var businessCharges = NewOrderWithItems();
        Register(businessCharges, Setting(), PlatformCollectedBy.Business);
        AssertRefused(OrderPaymentMethodRules.PlatformOnlyOnCreationMessage,
            () => businessCharges.RegisterPayment(100m, OrderPaymentMethod.Platform, Now, null, null, null, null));

        var platformCharged = NewOrderWithItems();
        Register(platformCharged, Setting());
        foreach (var method in new[] { OrderPaymentMethod.Cash, OrderPaymentMethod.Card, OrderPaymentMethod.Other })
            AssertRefused(PlatformOrderRules.PlatformChargedMessage,
                () => platformCharged.RegisterPayment(1m, method, Now, null, null, null, null));
        Assert.Single(platformCharged.Payments);
        Assert.Equal(platformCharged.Total, platformCharged.PaidAmount);
    }

    [Fact]
    public void RegisterPayment_ShouldRefuseATip_OnAnOrderOfAPlatform_WithOrWithoutItsDetail()
    {
        var registered = NewOrderWithItems();
        Register(registered, Setting(), PlatformCollectedBy.Business);
        AssertRefused(PlatformOrderRules.NoTipMessage,
            () => registered.RegisterPayment(registered.Total, OrderPaymentMethod.Card, Now, null, null, null, null, tip: 20m));

        // Loaded without its detail, as a payment loads it: the rule depends on the channel.
        var withoutDetail = NewOrderWithItems();
        AssertRefused(PlatformOrderRules.NoTipMessage,
            () => withoutDetail.RegisterPayment(100m, OrderPaymentMethod.Cash, Now, null, null, null, null, tip: 10m));
        withoutDetail.RegisterPayment(100m, OrderPaymentMethod.Cash, Now, null, null, null, null);
        Assert.Equal(100m, withoutDetail.PaidAmount);

        // A counter sale keeps its tip.
        var counter = new Order(
            Tenant, null, null, true, Order.NumberNotAssigned, BusinessType.Food, OrderType.Retail, "Público en general",
            null, null, null);
        counter.AddItem(Guid.NewGuid(), "Tacos", 1, 100m);
        counter.RegisterPayment(counter.Total, OrderPaymentMethod.Card, Now, null, null, null, null, tip: 15m);
        Assert.Equal(15m, counter.TipTotal);
    }

    [Fact]
    public void RegisterRefund_ShouldGiveBackAsPlatform_OnlyWhatThePlatformCharged()
    {
        var platformCharged = NewOrderWithItems();
        Register(platformCharged, Setting());
        AssertRefused(PlatformOrderRules.PlatformChargedRefundMessage, () => Refund(platformCharged, 100m, OrderPaymentMethod.Cash));
        AssertRefused(PlatformOrderRules.PlatformChargedRefundMessage, () => Refund(platformCharged, 100m, OrderPaymentMethod.Card));
        var refund = Refund(platformCharged, platformCharged.Total, OrderPaymentMethod.Platform);
        Assert.Equal(OrderPaymentMethod.Platform, refund.Method);
        Assert.Equal(platformCharged.Total, platformCharged.RefundedAmount);
        Assert.Equal(0m, platformCharged.NetPaidAmount);

        var counter = new Order(
            Tenant, null, null, true, Order.NumberNotAssigned, BusinessType.Food, OrderType.Retail, "Público en general",
            null, null, null);
        counter.AddItem(Guid.NewGuid(), "Tacos", 1, 100m);
        counter.RegisterPayment(counter.Total, OrderPaymentMethod.Cash, Now, null, null, null, null);
        AssertRefused(PlatformOrderRules.PlatformRefundOnlyOfPlatformMessage, () => Refund(counter, 50m, OrderPaymentMethod.Platform));
        Assert.Equal(OrderPaymentMethod.Cash, Refund(counter, 50m, OrderPaymentMethod.Cash).Method);

        // When the business charged, its refund goes through a drawer like any other.
        var businessCharged = NewOrderWithItems();
        Register(businessCharged, Setting(), PlatformCollectedBy.Business);
        businessCharged.RegisterPayment(businessCharged.Total, OrderPaymentMethod.Cash, Now, null, null, null, null);
        AssertRefused(PlatformOrderRules.PlatformRefundOnlyOfPlatformMessage,
            () => Refund(businessCharged, 100m, OrderPaymentMethod.Platform));
        Assert.Equal(OrderPaymentMethod.Cash, Refund(businessCharged, 100m, OrderPaymentMethod.Cash).Method);
    }

    [Fact]
    public void Void_ShouldRefundWhatThePlatformCharged_AndCancel()
    {
        var stateMachine = new FoodOrderStateMachine();
        var order = NewOrderWithItems();
        order.AdvanceOnCreation(OrderStatus.Confirmed, stateMachine, "cajero");
        Register(order, Setting());

        Refund(order, order.Total, OrderPaymentMethod.Platform);
        order.Cancel(stateMachine, "cajero", "Rappi lo canceló");

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(order.PaidAmount, order.RefundedAmount);
    }

    [Fact]
    public void MarkAsCreated_ShouldRequireTheFolio_AndTellItWithTheChannelAndWhoCharged()
    {
        var withoutDetail = NewOrderWithItems();
        withoutDetail.AssignNumber("ORD-000123");
        AssertRefused(PlatformOrderRules.MissingFolioMessage, withoutDetail.MarkAsCreated);
        Assert.Empty(withoutDetail.DomainEvents);

        var order = NewOrderWithItems(channel: OrderChannel.UberEats);
        order.AssignNumber("ORD-000124");
        Register(order, Setting(OrderChannel.UberEats), PlatformCollectedBy.Business, "bc953");
        order.MarkAsCreated();

        var created = Assert.IsType<OrderCreatedEvent>(Assert.Single(order.DomainEvents));
        Assert.Equal(OrderChannel.UberEats, created.Channel);
        Assert.Equal("BC953", created.ExternalOrderNumber);
        Assert.Equal(PlatformCollectedBy.Business, created.CollectedBy);
        Assert.Equal("ORD-000124", created.OrderNumber);
        Assert.Equal(order.Total, created.Total);
    }

    [Fact]
    public void MarkAsCreated_OfAnotherChannel_ShouldCarryNoFolio()
    {
        var counter = new Order(
            Tenant, null, null, true, Order.NumberNotAssigned, BusinessType.Food, OrderType.Retail, "Público en general",
            null, null, null);
        counter.AddItem(Guid.NewGuid(), "Tacos", 1, 100m);
        counter.AssignNumber("ORD-000125");

        counter.MarkAsCreated();

        var created = Assert.IsType<OrderCreatedEvent>(Assert.Single(counter.DomainEvents));
        Assert.Equal(OrderChannel.Counter, created.Channel);
        Assert.Null(created.ExternalOrderNumber);
        Assert.Null(created.CollectedBy);
    }

    [Fact]
    public void LinkToCustomerUser_ShouldBeRefused_ForAnOrderOfAPlatform()
    {
        var order = NewOrderWithItems();
        AssertRefused(PlatformOrderRules.NoCustomerMessage, () => order.LinkToCustomerUser(Guid.NewGuid()));
        Assert.Null(order.CustomerUserId);

        var online = new Order(
            Tenant, null, Guid.NewGuid(), false, Order.NumberNotAssigned, BusinessType.Food, OrderType.Retail, "Ana",
            "5512345678", null, null, channel: OrderChannel.Online);
        var user = Guid.NewGuid();
        online.LinkToCustomerUser(user);
        Assert.Equal(user, online.CustomerUserId);
        Assert.Null(online.TemporaryCustomerId);
    }

    [Fact]
    public void CloseIfPaidCounterSale_WithoutKitchen_ShouldDeliverWhatThePlatformCharged()
    {
        var stateMachine = new FoodOrderStateMachine();
        var order = NewOrderWithItems();
        order.AdvanceOnCreation(OrderStatus.Confirmed, stateMachine, "cajero");
        Register(order, Setting());

        Assert.True(order.CloseIfPaidCounterSale(stateMachine, waitsForKitchen: false, "cajero"));
        Assert.Equal(OrderStatus.Delivered, order.Status);
    }

    [Fact]
    public void CloseIfPaidCounterSale_WithKitchen_ShouldWaitUntilItIsReady()
    {
        var stateMachine = new FoodOrderStateMachine();
        var order = NewOrderWithItems();
        order.AdvanceOnCreation(OrderStatus.Confirmed, stateMachine, "cajero");
        Register(order, Setting());

        Assert.False(order.CloseIfPaidCounterSale(stateMachine, waitsForKitchen: true, "cajero"));
        Assert.Equal(OrderStatus.Confirmed, order.Status);

        order.TransitionTo(OrderStatus.Ready, stateMachine, "cocina");
        Assert.True(order.CloseIfPaidCounterSale(stateMachine, waitsForKitchen: true, "cocina"));
        Assert.Equal(OrderStatus.Delivered, order.Status);
    }

    [Fact]
    public void CloseIfPaidCounterSale_ShouldLeaveOpen_WhatTheBusinessHasNotChargedYet()
    {
        var stateMachine = new FoodOrderStateMachine();
        var order = NewOrderWithItems();
        order.AdvanceOnCreation(OrderStatus.Confirmed, stateMachine, "cajero");
        Register(order, Setting(), PlatformCollectedBy.Business);

        Assert.False(order.CloseIfPaidCounterSale(stateMachine, waitsForKitchen: false, "cajero"));
        Assert.Equal(OrderStatus.Confirmed, order.Status);

        order.RegisterPayment(order.Total, OrderPaymentMethod.Cash, Now, null, null, null, null);
        Assert.True(order.CloseIfPaidCounterSale(stateMachine, waitsForKitchen: false, "cajero"));
        Assert.Equal(OrderStatus.Delivered, order.Status);
    }

    private static Order NewOrder(
        OrderChannel channel = OrderChannel.Rappi,
        bool createdByBusiness = true,
        Guid? customerUserId = null,
        Guid? temporaryCustomerId = null,
        string? customerPhone = null,
        string? tableNumber = null,
        OrderType orderType = OrderType.Retail)
        => new(
            Tenant, customerUserId, temporaryCustomerId, createdByBusiness, Order.NumberNotAssigned, BusinessType.Food,
            orderType, "Juan", customerPhone, null, tableNumber, pricesIncludeTax: true, channel: channel);

    /// <summary>A paquete of $1,000 with IVA included: subtotal 862.07 and IVA 137.93.</summary>
    private static Order NewOrderWithItems(OrderChannel channel = OrderChannel.Rappi)
    {
        var order = NewOrder(channel);
        order.AddItem(Guid.NewGuid(), "Paquete familiar", 1, 1000m);
        return order;
    }

    private static DeliveryPlatformSetting Setting(OrderChannel channel = OrderChannel.Rappi, bool isActive = true, Guid? tenantId = null)
        => new(tenantId ?? Tenant, channel, isActive, PlatformTerms.WithFiscalDefaults(30m));

    private static PlatformOrderDetail Register(
        Order order,
        DeliveryPlatformSetting setting,
        PlatformCollectedBy collectedBy = PlatformCollectedBy.Platform,
        string folio = "BC953")
        => order.RegisterPlatformOrder(setting, PlatformOrderNumber.From(folio), collectedBy, Now, Location);

    private static OrderRefund Refund(Order order, decimal amount, OrderPaymentMethod method)
        => order.RegisterRefund(amount, method, "Rappi lo canceló", Now, "cajero", Location, null, restockItems: false, []);

    private static void AssertRefused(string message, Action action)
        => Assert.Equal(message, Assert.Throws<DomainException>(action).Message);
}
