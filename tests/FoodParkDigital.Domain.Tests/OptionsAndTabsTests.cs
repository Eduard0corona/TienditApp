using FoodParkDigital.Domain.Catalog.Entities;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Dining.Entities;
using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Orders;
using FoodParkDigital.Domain.Orders.Entities;
using FoodParkDigital.Domain.Orders.Enums;
using FoodParkDigital.Domain.Orders.Events;
using FoodParkDigital.Domain.Orders.StateMachines;
using FoodParkDigital.Domain.Tenants.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>Variants and extras of a product, and open tabs served in rounds.</summary>
public class OptionsAndTabsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void OptionGroups_ShouldKeepTheIdsOfWhatStillExists()
    {
        var latte = NewLatte();
        var size = latte.OptionGroups.Single(g => g.Name == "Tamaño");
        var grande = size.Options.Single(o => o.Name == "Grande");

        latte.SetOptionGroups(
        [
            new ProductOptionGroupDefinition(size.Id, "Tamaño", 1, 1,
            [
                new ProductOptionDefinition(grande.Id, "Grande", 12m),
                new ProductOptionDefinition(null, "Extra grande", 20m)
            ])
        ]);

        var updated = Assert.Single(latte.OptionGroups);
        Assert.Equal(size.Id, updated.Id);
        Assert.Equal(grande.Id, updated.Options.First().Id);
        Assert.Equal(12m, updated.Options.First().PriceDelta);
        Assert.Equal(["Grande", "Extra grande"], updated.Options.Select(o => o.Name));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(0, 3)]
    [InlineData(-1, 1)]
    public void OptionGroups_ShouldRejectImpossibleSelections(int min, int max)
    {
        var latte = NewLatte();

        Assert.Throws<DomainException>(() => latte.SetOptionGroups(
        [
            new ProductOptionGroupDefinition(null, "Leche", min, max,
            [
                new ProductOptionDefinition(null, "Entera", 0m),
                new ProductOptionDefinition(null, "Almendra", 8m)
            ])
        ]));
    }

    [Fact]
    public void OptionGroups_ShouldRejectRepeatedNamesAndThirdDecimals()
    {
        var latte = NewLatte();

        Assert.Throws<DomainException>(() => latte.SetOptionGroups(
        [
            new ProductOptionGroupDefinition(null, "Leche", 0, 1, [new ProductOptionDefinition(null, "Entera", 0m), new ProductOptionDefinition(null, "entera", 0m)])
        ]));
        Assert.Throws<DomainException>(() => latte.SetOptionGroups(
        [
            new ProductOptionGroupDefinition(null, "Leche", 0, 1, [new ProductOptionDefinition(null, "Almendra", 8.005m)])
        ]));
    }

    [Fact]
    public void ResolveOptions_ShouldPriceTheChosenVariantAndExtras()
    {
        var latte = NewLatte();
        var grande = Option(latte, "Grande");
        var almond = Option(latte, "Almendra");
        var shot = Option(latte, "Shot extra");

        var options = latte.ResolveOptions([shot.Id, grande.Id, almond.Id]);

        Assert.Equal(["Grande", "Almendra", "Shot extra"], options.Select(o => o.OptionName));
        Assert.Equal(55m + 12m + 8m + 10m, latte.PriceWith(options));
    }

    [Fact]
    public void ResolveOptions_ShouldEnforceEachGroup()
    {
        var latte = NewLatte();
        var grande = Option(latte, "Grande");
        var chico = Option(latte, "Chico");

        var missingSize = Assert.Throws<DomainException>(() => latte.ResolveOptions([]));
        Assert.Contains("Tamaño", missingSize.Message);
        Assert.Throws<DomainException>(() => latte.ResolveOptions([grande.Id, chico.Id]));
        Assert.Throws<DomainException>(() => latte.ResolveOptions([grande.Id, grande.Id]));
        Assert.Throws<DomainException>(() => latte.ResolveOptions([grande.Id, Guid.NewGuid()]));
    }

    [Fact]
    public void ResolveOptions_ShouldRejectUnavailableOptions()
    {
        var latte = NewLatte();
        var size = latte.OptionGroups.Single(g => g.Name == "Tamaño");
        latte.SetOptionGroups(latte.OptionGroups.Select(group => new ProductOptionGroupDefinition(
            group.Id, group.Name, group.MinSelections, group.MaxSelections,
            group.Options.Select(o => new ProductOptionDefinition(o.Id, o.Name, o.PriceDelta, IsAvailable: o.Name != "Grande")).ToList())).ToList());

        Assert.Throws<DomainException>(() => latte.ResolveOptions([Option(latte, "Grande").Id]));
        Assert.Single(latte.ResolveOptions([size.Options.Single(o => o.Name == "Chico").Id]));
    }

    [Fact]
    public void PriceWith_ShouldNeverGoBelowZero()
    {
        var water = new Product(TenantId, Guid.NewGuid(), "Agua", null, 10m, "MXN");
        water.SetOptionGroups([new ProductOptionGroupDefinition(null, "Promoción", 0, 1, [new ProductOptionDefinition(null, "Gratis de más", -15m)])]);

        var options = water.ResolveOptions([water.OptionGroups.Single().Options.Single().Id]);

        Assert.Throws<DomainException>(() => water.PriceWith(options));
    }

    [Fact]
    public void Line_ShouldKeepItsOptionsAndPriceThem()
    {
        var latte = NewLatte();
        var options = latte.ResolveOptions([Option(latte, "Grande").Id, Option(latte, "Almendra").Id]);
        var order = NewFoodOrder();

        order.AddItem(new OrderLineRequest(latte.Id, latte.Name, 2, latte.PriceWith(options), null, VatRate.General, OrderLineFiscalData.From(latte), options));

        var line = Assert.Single(order.Items);
        Assert.Equal(75m, line.UnitPrice);
        Assert.Equal(150m, line.TotalPrice);
        Assert.Equal("Latte (Grande, Almendra)", line.Describe());
        Assert.All(line.Options, option => Assert.Equal(TenantId, option.TenantId));
    }

    [Fact]
    public void Tab_ShouldOpenWithItsFirstRoundInTheKitchen()
    {
        var table = new DiningTable(TenantId, "Mesa 4", "Terraza", 4, 1);
        var order = NewFoodOrder();
        order.AddItem(Guid.NewGuid(), "Tacos al pastor", 3, 25m);

        order.OpenAsTab(table.Id, table.Name, DateTimeOffset.UtcNow, "mesero");

        Assert.True(order.IsTab);
        Assert.True(order.IsOpenTab);
        Assert.Equal(table.Id, order.OpenTableId);
        Assert.Equal("Mesa 4", order.TableNumber);
        var ticket = Assert.Single(order.KitchenTickets);
        Assert.Equal(1, ticket.Round);
        Assert.Equal(KitchenTicketStatus.Pending, ticket.Status);
    }

    [Fact]
    public void Tab_ShouldNeedItemsAndANewOrder()
    {
        var empty = NewFoodOrder();
        Assert.Throws<DomainException>(() => empty.OpenAsTab(null, "Barra", DateTimeOffset.UtcNow, null));

        var confirmed = NewFoodOrder();
        confirmed.AddItem(Guid.NewGuid(), "Cerveza", 1, 45m);
        confirmed.TransitionTo(OrderStatus.Confirmed, new FoodOrderStateMachine(), "caja");
        Assert.Throws<DomainException>(() => confirmed.OpenAsTab(null, "Barra", DateTimeOffset.UtcNow, null));
    }

    [Fact]
    public void Rounds_ShouldAddLinesTicketsAndTotals()
    {
        var order = OpenTab();

        var ticket = order.AddRound(
            [new OrderLineRequest(Guid.NewGuid(), "Cerveza", 2, 45m, null, VatRate.General, OrderLineFiscalData.Default, [])],
            DateTimeOffset.UtcNow,
            "mesero");

        Assert.Equal(2, ticket.Round);
        Assert.Equal(2, order.CurrentRound);
        Assert.Equal(2, order.KitchenTickets.Count);
        Assert.Equal([1, 2], order.Items.Select(i => i.Round));
        Assert.Equal(75m + 90m, order.Total);
        Assert.Contains(order.DomainEvents, e => e is TabRoundSentEvent { Round: 2 });
    }

    [Fact]
    public void CloseTab_ShouldNeedTheBillPaidAndFreeTheTable()
    {
        var order = OpenTab();
        var machine = OrderStateMachineFactory.Create(order.BusinessType, order.OrderType);

        Assert.Throws<DomainException>(() => order.CloseTab(machine, "mesero"));

        order.RegisterPayment(order.Total, OrderPaymentMethod.Cash, DateTimeOffset.UtcNow, null, null, null, null);
        order.CloseTab(machine, "mesero");

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.False(order.IsOpenTab);
        Assert.Null(order.OpenTableId);
        Assert.Throws<DomainException>(() => order.AddRound(
            [new OrderLineRequest(Guid.NewGuid(), "Postre", 1, 60m, null, VatRate.General, OrderLineFiscalData.Default, [])],
            DateTimeOffset.UtcNow, "mesero"));
    }

    [Fact]
    public void PaidTab_ShouldStayOpenUntilTheTableLeaves()
    {
        var order = OpenTab();
        var machine = OrderStateMachineFactory.Create(order.BusinessType, order.OrderType);
        order.RegisterPayment(order.Total, OrderPaymentMethod.Cash, DateTimeOffset.UtcNow, null, null, null, null);

        var closed = order.CloseIfPaidCounterSale(machine, waitsForKitchen: false, "caja");

        Assert.False(closed);
        Assert.True(order.IsOpenTab);
    }

    [Fact]
    public void CancelledTab_ShouldFreeTheTable()
    {
        var order = OpenTab();

        order.Cancel(OrderStateMachineFactory.Create(order.BusinessType, order.OrderType), "gerente", "Se fueron");

        Assert.Null(order.OpenTableId);
        Assert.False(order.IsOpenTab);
    }

    [Fact]
    public void KitchenTicket_ShouldOnlyMoveForward()
    {
        var order = OpenTab();
        var ticket = order.KitchenTickets.Single();

        order.ChangeKitchenTicketStatus(ticket.Id, KitchenTicketStatus.Ready, DateTimeOffset.UtcNow);

        Assert.Equal(KitchenTicketStatus.Ready, ticket.Status);
        Assert.NotNull(ticket.StartedAt);
        Assert.NotNull(ticket.ReadyAt);
        Assert.Throws<DomainException>(() => order.ChangeKitchenTicketStatus(ticket.Id, KitchenTicketStatus.Preparing, DateTimeOffset.UtcNow));
        order.ChangeKitchenTicketStatus(ticket.Id, KitchenTicketStatus.Served, DateTimeOffset.UtcNow);
        Assert.NotNull(ticket.ServedAt);
    }

    [Fact]
    public void MoveToTable_ShouldChangeTheTableOfAnOpenTab()
    {
        var order = OpenTab();
        var terrace = new DiningTable(TenantId, "Terraza 2", null, null, 5);

        order.MoveToTable(terrace.Id, terrace.Name);

        Assert.Equal(terrace.Id, order.OpenTableId);
        Assert.Equal(terrace.Id, order.DiningTableId);
        Assert.Equal("Terraza 2", order.TableNumber);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("Una mesa con un nombre larguísimo", null)]
    [InlineData("Mesa 1", 0)]
    public void Table_ShouldValidateItsData(string name, int? seats)
    {
        Assert.Throws<DomainException>(() => new DiningTable(TenantId, name, null, seats, 0));
    }

    private static Order OpenTab()
    {
        var order = NewFoodOrder();
        order.AddItem(Guid.NewGuid(), "Tacos al pastor", 3, 25m);
        order.OpenAsTab(Guid.NewGuid(), "Mesa 1", DateTimeOffset.UtcNow, "mesero");
        order.TransitionTo(OrderStatus.Confirmed, new FoodOrderStateMachine(), "mesero");
        return order;
    }

    private static Product NewLatte()
    {
        var latte = new Product(TenantId, Guid.NewGuid(), "Latte", null, 55m, "MXN");
        latte.SetOptionGroups(
        [
            new ProductOptionGroupDefinition(null, "Tamaño", 1, 1,
            [
                new ProductOptionDefinition(null, "Chico", -5m),
                new ProductOptionDefinition(null, "Grande", 12m)
            ]),
            new ProductOptionGroupDefinition(null, "Leche", 0, 1,
            [
                new ProductOptionDefinition(null, "Deslactosada", 0m),
                new ProductOptionDefinition(null, "Almendra", 8m)
            ]),
            new ProductOptionGroupDefinition(null, "Extras", 0, 3,
            [
                new ProductOptionDefinition(null, "Shot extra", 10m),
                new ProductOptionDefinition(null, "Crema batida", 7m),
                new ProductOptionDefinition(null, "Canela", 0m)
            ])
        ]);
        return latte;
    }

    private static ProductOption Option(Product product, string name)
        => product.OptionGroups.SelectMany(g => g.Options).Single(o => o.Name == name);

    private static Order NewFoodOrder() => new(
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
        pricesIncludeTax: true);
}
