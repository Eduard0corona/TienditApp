using FoodParkDigital.Domain.Catalog.Enums;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// A dish of an open tab cancelled after it went to the kitchen: the tab stops charging it, the line keeps what was
/// ordered and the cancellation is its own record. Only the part not paid yet can be cancelled.
/// </summary>
public class TabItemCancellationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 20, 15, 0, TimeSpan.Zero);

    [Fact]
    public void CancellingPartOfADish_ShouldLowerTheTabAndKeepWhatWasOrdered()
    {
        var order = OpenTab(pricesIncludeTax: true);
        var tacos = order.Items.Single();

        var cancellation = order.CancelTabItem(tacos.Id, 1, "  Se equivocó el mesero  ", returnToStock: true, Now, "Ana");

        Assert.Equal(3, tacos.Quantity);
        Assert.Equal(75m, tacos.TotalPrice);
        Assert.Equal(1, tacos.CancelledQuantity);
        Assert.Equal(25m, tacos.CancelledAmount);
        Assert.Equal(2, tacos.ActiveQuantity);
        Assert.Equal(50m, tacos.ChargedPrice);
        Assert.Equal(50m, order.Total);
        Assert.Equal(50m, order.Balance);
        Assert.Equal(Now, order.UpdatedAt);

        Assert.Same(cancellation, Assert.Single(order.ItemCancellations));
        Assert.Equal(TenantId, cancellation.TenantId);
        Assert.Equal(order.Id, cancellation.OrderId);
        Assert.Equal(tacos.Id, cancellation.OrderItemId);
        Assert.Equal(1, cancellation.Round);
        Assert.Equal(1, cancellation.Quantity);
        Assert.Equal(25m, cancellation.Amount);
        Assert.Equal("Se equivocó el mesero", cancellation.Reason);
        Assert.True(cancellation.ReturnedToStock);
        Assert.Equal(Now, cancellation.CancelledAt);
        Assert.Equal("Ana", cancellation.CancelledBy);

        var cancelled = Assert.Single(order.DomainEvents.OfType<TabItemCancelledEvent>());
        Assert.Equal(new TabItemCancelledEvent(
            order.Id, TenantId, cancellation.Id, tacos.Id, 1, "Tacos al pastor", 1, 25m, "Se equivocó el mesero", true) with
        {
            EventId = cancelled.EventId,
            OccurredOn = cancelled.OccurredOn
        }, cancelled);
    }

    [Fact]
    public void CancellingAllOfADish_ShouldLeaveItOutOfTheBillAndItsTaxes()
    {
        var order = OpenTab(pricesIncludeTax: false);
        var beer = AddRound(order, "Cerveza", 2, 45m, VatRate.General);
        var water = AddRound(order, "Agua", 1, 20m, VatRate.Of(0m));

        order.CancelTabItem(water.Id, 1, "Ya no la quiso", returnToStock: true, Now, "Ana");

        Assert.Equal(0m, water.ChargedPrice);
        Assert.Equal(20m, water.CancelledAmount);
        // Only the 16 % lines are left: 75 + 90 plus their IVA.
        var line = Assert.Single(order.GetTaxBreakdown());
        Assert.Equal(16m, line.RatePercent);
        Assert.Equal(165m, line.TaxableBase);
        Assert.Equal(26.40m, line.TaxAmount);
        Assert.Equal(191.40m, order.Total);

        order.CancelTabItem(beer.Id, 2, "Se acabó el barril", returnToStock: false, Now, "Ana");

        Assert.Equal(87m, order.Total);
        Assert.Equal(2, order.ItemCancellations.Count);
        Assert.False(order.ItemCancellations.Last().ReturnedToStock);
    }

    [Fact]
    public void WeighedDish_ShouldChargeWhatIsLeftRoundedOnceAndCancelToZero()
    {
        var order = OpenTab(pricesIncludeTax: true);
        var steak = AddRound(order, "Arrachera", 1.255m, 389.90m, VatRate.General, UnitType.Kg);
        Assert.Equal(489.32m, steak.TotalPrice);

        order.CancelTabItem(steak.Id, 0.505m, "Se pasó de cocción", returnToStock: false, Now, "Ana");

        // 0.750 kg left at $389.90 is $292.425: banker's rounding charges $292.42.
        Assert.Equal(292.42m, steak.ChargedPrice);
        Assert.Equal(196.90m, steak.CancelledAmount);

        order.CancelTabItem(steak.Id, 0.75m, "Ya no la quisieron", returnToStock: false, Now, "Ana");

        Assert.Equal(0m, steak.ChargedPrice);
        Assert.Equal(steak.TotalPrice, order.ItemCancellations.Sum(c => c.Amount));
        Assert.Equal(75m, order.Total);
    }

    [Fact]
    public void PaidPart_ShouldBeRefundedNotCancelled()
    {
        var order = OpenTab(pricesIncludeTax: true);
        var tacos = order.Items.Single();
        order.RegisterPayment(40m, OrderPaymentMethod.Cash, Now, null, null, null, null);

        // $75 with $40 paid: one taco less leaves $50, still above what was paid.
        order.CancelTabItem(tacos.Id, 1, "Uno salió frío", returnToStock: false, Now, "Ana");
        Assert.Equal(10m, order.Balance);

        var refused = Assert.Throws<DomainException>(() => order.CancelTabItem(tacos.Id, 1, "Otro salió frío", false, Now, "Ana"));

        Assert.Contains("$40.00", refused.Message);
        Assert.Contains("$25.00", refused.Message);
        Assert.Contains("reembólsalo", refused.Message);
        Assert.Equal(50m, order.Total);
        Assert.Equal(1, tacos.CancelledQuantity);
        Assert.Single(order.ItemCancellations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(1.5)]
    public void ImpossibleQuantities_ShouldBeRefusedWithoutChangingTheTab(decimal quantity)
    {
        var order = OpenTab(pricesIncludeTax: true);
        var tacos = order.Items.Single();

        Assert.Throws<DomainException>(() => order.CancelTabItem(tacos.Id, quantity, "Se equivocó", true, Now, "Ana"));

        AssertUnchanged(order, tacos);
    }

    [Fact]
    public void CancelledUnits_ShouldNotBeCancelledTwiceNorReturnedAfterwards()
    {
        var order = OpenTab(pricesIncludeTax: true);
        var tacos = order.Items.Single();
        order.CancelTabItem(tacos.Id, 2, "Se equivocó el mesero", true, Now, "Ana");

        var twice = Assert.Throws<DomainException>(() => order.CancelTabItem(tacos.Id, 2, "Otra vez", true, Now, "Ana"));
        Assert.Contains("solo quedan 1", twice.Message);
        Assert.Equal(1, tacos.ReturnableQuantity);

        var machine = OrderStateMachineFactory.Create(order.BusinessType, order.OrderType);
        order.RegisterPayment(order.Total, OrderPaymentMethod.Cash, Now, null, null, null, null);
        order.CloseTab(machine, "Ana");

        Assert.Throws<DomainException>(() => order.RegisterRefund(
            25m, OrderPaymentMethod.Cash, "Devolvió los tacos", Now, "Ana", null, null, true, [(tacos.Id, 2m)]));
        order.RegisterRefund(25m, OrderPaymentMethod.Cash, "Devolvió el taco", Now, "Ana", null, null, true, [(tacos.Id, 1m)]);
        Assert.Equal(0, tacos.ReturnableQuantity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("no")]
    public void Reason_ShouldBeRequired(string reason)
    {
        var order = OpenTab(pricesIncludeTax: true);
        var tacos = order.Items.Single();

        Assert.Throws<DomainException>(() => order.CancelTabItem(tacos.Id, 1, reason, true, Now, "Ana"));

        AssertUnchanged(order, tacos);
    }

    [Fact]
    public void LongReason_ShouldBeCutToItsLimit()
    {
        var order = OpenTab(pricesIncludeTax: true);

        var cancellation = order.CancelTabItem(order.Items.Single().Id, 1, new string('x', 600), true, Now, "Ana");

        Assert.Equal(OrderItemCancellation.MaxReasonLength, cancellation.Reason.Length);
    }

    [Fact]
    public void OnlyAnOpenTab_ShouldCancelItsDishes()
    {
        var unknown = OpenTab(pricesIncludeTax: true);
        Assert.Throws<DomainException>(() => unknown.CancelTabItem(Guid.NewGuid(), 1, "Se equivocó", true, Now, "Ana"));
        Assert.Throws<DomainException>(() => unknown.CancelTabItem(unknown.Items.Single().Id, 1, "Se equivocó", true, Now, " "));

        var closed = OpenTab(pricesIncludeTax: true);
        closed.RegisterPayment(closed.Total, OrderPaymentMethod.Cash, Now, null, null, null, null);
        closed.CloseTab(OrderStateMachineFactory.Create(closed.BusinessType, closed.OrderType), "Ana");
        Assert.Throws<DomainException>(() => closed.CancelTabItem(closed.Items.Single().Id, 1, "Se equivocó", true, Now, "Ana"));

        var cancelled = OpenTab(pricesIncludeTax: true);
        cancelled.Cancel(OrderStateMachineFactory.Create(cancelled.BusinessType, cancelled.OrderType), "Ana", "Se fueron");
        Assert.Throws<DomainException>(() => cancelled.CancelTabItem(cancelled.Items.Single().Id, 1, "Se equivocó", true, Now, "Ana"));

        var sale = NewFoodOrder(pricesIncludeTax: true);
        sale.AddItem(Guid.NewGuid(), "Tacos al pastor", 3, 25m);
        Assert.Throws<DomainException>(() => sale.CancelTabItem(sale.Items.Single().Id, 1, "Se equivocó", true, Now, "Ana"));

        foreach (var order in new[] { unknown, closed, cancelled, sale })
        {
            Assert.Empty(order.ItemCancellations);
            Assert.Empty(order.DomainEvents.OfType<TabItemCancelledEvent>());
        }
    }

    private static void AssertUnchanged(Order order, OrderItem tacos)
    {
        Assert.Equal(75m, order.Total);
        Assert.Equal(0, tacos.CancelledQuantity);
        Assert.Equal(0m, tacos.CancelledAmount);
        Assert.Empty(order.ItemCancellations);
        Assert.Empty(order.DomainEvents.OfType<TabItemCancelledEvent>());
    }

    private static OrderItem AddRound(Order order, string name, decimal quantity, decimal unitPrice, VatRate rate, UnitType unit = UnitType.Unit)
    {
        var fiscal = new OrderLineFiscalData(unit, SatCatalog.GenericProductCode, unit == UnitType.Kg ? "KGM" : "H87", CfdiTaxObject.Subject);
        order.AddRound([new OrderLineRequest(Guid.NewGuid(), name, quantity, unitPrice, null, rate, fiscal, [])], Now, "mesero");
        return order.Items.Last();
    }

    private static Order OpenTab(bool pricesIncludeTax)
    {
        var order = NewFoodOrder(pricesIncludeTax);
        order.AddItem(Guid.NewGuid(), "Tacos al pastor", 3, 25m);
        order.OpenAsTab(Guid.NewGuid(), "Mesa 1", Now, "mesero");
        order.TransitionTo(OrderStatus.Confirmed, new FoodOrderStateMachine(), "mesero");
        order.ClearDomainEvents();
        return order;
    }

    private static Order NewFoodOrder(bool pricesIncludeTax) => new(
        tenantId: TenantId,
        customerUserId: null,
        temporaryCustomerId: null,
        createdByBusiness: true,
        orderNumber: "ORD-000001",
        businessType: BusinessType.Food,
        orderType: OrderType.Retail,
        customerName: "Público en general",
        customerPhone: null,
        customerNotes: null,
        tableNumber: null,
        pricesIncludeTax: pricesIncludeTax);
}
